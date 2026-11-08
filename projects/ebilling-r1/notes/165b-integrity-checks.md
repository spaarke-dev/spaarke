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

## 7. 165b-4 design (for ontology-r1)

Task `tasks/165b4-spend-server-written-sources.poml`, step 1: the design note, before any code. Base `work/ebilling-r1` at `c66b6933cf` (165b-3
merged). Wording stays neutral: every spend reader decides only from server-written sources. Line numbers are on that base.

### 7.1 The readers today

| Reader | Counted spend | Pending spend | Identity |
|---|---|---|---|
| `FinanceBaseValues.ReadAsync` (`FinanceBaseValues.cs:262-285`), called by `SignalEvaluator.cs:233-234` | 3 grouped FetchXML reads per chunk of 200 parents (`ReadSpendPartsAsync` `:328-359`: billed part, approved part, billed re-read; 6 on a retry) | `ReadPendingSpendAsync` `:296-312`, same 3 reads with the held anti-join (`:416-428`). **No production caller** (only tests). | shared `IGenericEntityService` = the `IDataverseService` singleton (`GraphModule.cs:90,145`) |
| `FinanceRollupService` | 1 row read, `BuildInvoiceQuery` (`:361-374`), not paged (`:297-307`), summed in memory (`ComputeSpendFigures` `:256-292`) | 1 row read with the anti-join (`BuildPendingInvoiceQuery` `:327-352`); a fault leaves the stored value (`WriteRollupAsync` `:143-157`) | the same `IDataverseService`, unwrapped to `ServiceClient` (`:180`) |
| `Services/Finance/Tools/FinancialCalculationToolHandler` | 1 row read through the rollup's builder (`:131-132`, `:145-154`, `:190-199`) | none | the same; a fault becomes `CreateError` (`:120-124`) |

`Services/Ai/Handlers/FinancialCalculationToolHandler` (formula tool) reads no invoice and is out of scope. On dev the `IDataverseService` identity
is the BFF application identity (read 2026-11-08, §7.9).

### 7.2 The problem

uac-r2's F1 on 165b-3 (merged, §6.3 item 1) makes a counted status need the writer-created approve record **under the approval's key**:
`InvoiceLineOrigin.ReviewIdOf(row)` (`InvoiceLineOrigin.cs:140-155`, reads `sprk_steps` JSON) equals
`BillingDecisionKey.IdOf(sprk_invoice, X, InvoiceApprovalService.DecisionIdFor(portion of X))` (`DecisionRecordWriter.cs:233-242`,
`InvoiceApprovalService.cs:382`). FetchXML cannot match a value inside `sprk_steps`, and the key is a SHA-256 of a string built from the portion
id. So no `exists` link can carry the key; spend needs the portion read and the record read that `InvoicesWithApproveRecordAsync`
(`InvoiceApprovalService.cs:1403-1474`) makes, or a parent-scoped form of them.

**Note on §6.2.** §6.2 as merged records no dev probe of an `exists` link. §7.9 records the probe this note ran (read-only, 2026-11-08).

### 7.3 Options

Both options use the same keyed reads, scoped by parent (one read per chunk of parents, not per 200 invoices): portions of the chunk's counted
invoices, and writer-created approve records regarding them, each through an inner link to `sprk_invoice` (§7.5). With the 165b-3 read as it is
(scoped by invoice ids), each option costs one more invoice read plus `2 x ceil(n/200)` for n counted invoices.

| | **(a)** unkeyed `exists` link + keyed deny-list | **(b)** row read + keyed allow-list (recommended) |
|---|---|---|
| Counted spend | Keyed reads give F = invoices with a writer-created approve record but none under their key. Then the 3 grouped reads, each with the unkeyed `exists` link and `sprk_invoiceid not-in F` (omitted when F is empty; F larger than 200 throws, value unknown). | One paged row read of the chunk's counted invoices (id, parent, the two amount columns). Keyed reads give M = invoices with the record under their key. Sum `SpendAmount` over rows in M, per parent. |
| Queries per chunk (evaluator) | 2 keyed + 3 grouped (6 on a retry) + 1 budget = **6** (9) | 1 row + 2 keyed + 1 budget = **4**, no retry |
| Readers unable to disagree | Weaker. The sum is a grouped read; the key is applied as a deny-list from an earlier read. An invoice whose record appears between the keyed reads and the grouped reads is counted on the unkeyed link alone. The rollup and the tool still need rows (dates, count), so they would use (b)'s path anyway: two code paths. | One function, `FinanceBaseValues.ReadCountedInvoicesAsync`, returns the counted rows; `ReadAsync`, the rollup and the tool all sum or bucket those rows. An invoice counts only when it is in M (allow-list). A race leaves out an invoice whose record is not yet visible, which is what the rule says. S5 holds by construction (one row per invoice). |
| O1 (static, pure reader) | `ReadAsync(..., Guid writerUserId, ...)`; the keyed reads are a static call with `entities` and `writerUserId` as parameters. | Same. |
| `ReadSpendPartsAsync` re-read | Kept; the link and `not-in` are in all three reads, so an invoice whose record appears between them changes the billed part and the re-read catches it (§7.7). | Not needed: one row read is one snapshot of each invoice's amounts and `SpendAmount` coalesces per row. The grouped spend path is retired (§7.11 H5). |
| FinanceBaseValues hunk | Smaller (H1, H4 and a link in `BuildGroupedFetchXml`). | Larger (H1-H5). |
| Aggregate limit | Grouped reads keep the 50,000-row aggregate limit. | Row reads page at 5000; no aggregate limit. |

**Recommendation: (b).** It meets O7's budget exactly (4 per chunk, today's 3 + 1), the key is an allow-list applied per invoice, and the three
readers share one function, so parity is structural (the O6 test still runs). The cost is retiring the grouped spend path in ontology's file.
If ontology-r1 prefers the smaller hunk, (a) works at 6 queries per chunk, with the deny-list window stated as a known limit.

### 7.4 One keyed rule (no copy)

The keyed logic does **not** need to move into `Services/Signals/`. `FinanceBaseValues` already uses `Services/Finance` types, the writer rule stays
in `Services/Signals/WriterCreatedRecord.cs`, and the key stays in `BillingDecisionKey`. The extraction is ebilling's, in `Services/Finance`:

- **New** `Services/Finance/InvoiceApproveRecord.cs`: a pure static class with no DI registration. It holds:
  - `ActionCode` (moved from `InvoiceApprovalService.ApproveActionCode`, `:262`) and `InvoiceLookup` (moved from `:275`).
  - `RecordQuery(Guid writerUserId)`: action code plus `WriterCreatedRecord.AddConditions`, columns lookup and `sprk_steps`, paged, `NoLock`.
  - `PortionQuery()`.
  - The pure matcher `Matched(portions, records)`, the body of `:1426-1470`: the `keys` map with `BillingDecisionKey.IdOf(..., DecisionIdFor(portion))` and the `ReviewIdOf` compare.
  - `MatchedInvoicesAsync(entities, scope, writerUserId, ct)`, where `scope` is either invoice ids (chunks of 200: 165b-3's behaviour) or
    parents (parent lookup `In`, status `In`, through an inner link to `sprk_invoice`).
- **`InvoiceApprovalService` hunks.** `InvoicesWithApproveRecordAsync` (`:1403-1474`) keeps its signature and delegates to the invoice-id scope.
  `WriterApproveRecordsAsync` (`:1354-1373`) uses `RecordQuery` (one copy of the record conditions instead of three). `:262` and `:275` refer
  to the moved members. No behaviour change: the 165b-3 tests (`InvoiceApprovalCompletionTests`, `ApExportApprovalRecordTests`, Mark Paid) are
  the parity check for the move.
- **§11.** *Existing*: `InvoicesWithApproveRecordAsync` and `WriterApproveRecordsAsync`. *Extension*: an invoice-id-scoped internal of a
  DI service. Calling it from ontology's static reader would tie that reader to the approval service, and it has no parent scope. The fallback
  is a parent-scope overload on the same internal static, with no new type. *Cost of doing nothing*: spend copies the key match, so approval,
  AP export and spend can disagree.
- The writer id comes from `OntologyWriterDataverseClient.GetWriterUserIdAsync` (`:224`) in every reader. The evaluator gets it there (Q2).
  The rollup and the tool take `BillingDecisionRecorder` (singleton, `FinanceModule.cs:201`; `WriterUserIdAsync` `:174`), as `ApExportService`
  does. No new registration.

### 7.5 Counted spend reads as built

**(b), per chunk of up to 200 parents.** The three reads below, shown in FetchXML form (built as `QueryExpression`):

```xml
<!-- 1. rows -->
<fetch><entity name="sprk_invoice"><attribute name="sprk_invoiceid"/><attribute name="sprk_matter"/>
  <attribute name="sprk_approvedtotal"/><attribute name="sprk_totalamount"/>
  <filter type="and"><condition attribute="sprk_matter" operator="in"><value>{parent}</value>...</condition>
    <condition attribute="statuscode" operator="in"><value>659490000</value><value>659490001</value><value>659490002</value></condition></filter>
</entity></fetch>
<!-- 2. portions -->
<fetch><entity name="sprk_invoiceapproval"><attribute name="sprk_invoiceapprovalid"/><attribute name="sprk_invoice"/>
  <link-entity name="sprk_invoice" from="sprk_invoiceid" to="sprk_invoice" link-type="inner">{same parent + status filter}</link-entity>
</entity></fetch>
<!-- 3. writer-created approve records -->
<fetch><entity name="sprk_decisionrecord"><attribute name="sprk_regardinginvoice"/><attribute name="sprk_steps"/>
  <filter type="and"><condition attribute="sprk_actioncode" operator="eq" value="approve-invoice"/>
    {WriterCreatedRecord: createdby eq {writer}, createdonbehalfby null}</filter>
  <link-entity name="sprk_invoice" from="sprk_invoiceid" to="sprk_regardinginvoice" link-type="inner">{same parent + status filter}</link-entity>
</entity></fetch>
```

The rollup and the tool add `sprk_invoicedate` and `statuscode` to read 1 (their `InvoiceColumns`, `FinanceRollupService.cs:356-357`).

**(a), as it would be built** (`BuildGroupedSpendFetchXml`, one per part):

```xml
<fetch aggregate="true"><entity name="sprk_invoice">
  <attribute name="{sprk_approvedtotal|sprk_totalamount}" alias="total" aggregate="sum"/>
  <attribute name="sprk_matter" alias="parent" groupby="true"/>
  <filter type="and">{parent in; approvedtotal not-null|null; statuscode in counted}
    [<condition attribute="sprk_invoiceid" operator="not-in">{F}</condition>]</filter>
  <link-entity name="sprk_decisionrecord" from="sprk_regardinginvoice" to="sprk_invoiceid" link-type="exists">
    <filter type="and"><condition attribute="sprk_actioncode" operator="eq" value="approve-invoice"/>
      {WriterCreatedRecord.FetchXmlFilter(writer)}</filter></link-entity>
</entity></fetch>
```

### 7.6 Pending spend and the hold batch

- **Rule.** Pending leaves out every In Review invoice that `InvoiceBlockedHold.Of` (`InvoiceBlockedHold.cs:102-136`) holds, applied in memory.
  No copy: the batch read lives in `InvoiceBlockedHold`, a type `FindingsJsonAppOnlyReadGuardTests` has already reviewed (`:65`).
- **Reads, per chunk of up to 200 parents.**
  1. A paged row read of the In Review invoices (as §7.5 read 1, status In Review).
  2. One paged read of `sprk_invoicesubmission`, columns `InvoiceBlockedHold.SubmissionColumns` (`:72-76`), through an inner link to
     `sprk_invoice` with the same parent and status filter.
- **Count.** 2 per chunk, constant in the invoice count (paging adds one read per 5000 rows). Today the rollup makes 1 read and
  `ReadPendingSpendAsync` makes 3.
- **The anti-join is dropped.** `Of`'s check (1) is the same column rule (`MatchesHeldConditions`, `:245-263`), so there is one rule.
- **One pending reader.** The rollup calls `FinanceBaseValues.ReadPendingSpendAsync` with `[parentId]`.
- **Behaviour change (EB-D-201).** An In Review invoice with no linked own submission is held by `Of` (`HeldOwnSubmissionUnlinked`), so it leaves
  pending. On dev, both In Review invoices have a linked submission (§7.9).
- **Canary (O5), in the read, no extra query.** The persister writes the own submission's findings, with `invoiceId`, in the transaction that
  creates the invoice (D-130(a)). The undo clears the findings only together with the link, after deleting the invoice
  (`SubmissionCommitGate.cs:428-432`). So a linked own submission whose findings read back empty means the column cannot be read: the pending
  read throws (unknown), never "held" or "not held". Without the canary, an unreadable column would make `Of` hold every invoice and pending
  would read as 0. Q6 asks whether ontology-r1 also wants an explicit per-run check.

### 7.7 Query count per evaluation and the re-read check

| Path | Today | (a) | (b) |
|---|---|---|---|
| Evaluator, per chunk of up to 200 parents | 3 invoice (6 on a retry) + 1 budget | 2 keyed + 3 grouped (6 on a retry) + 1 budget | 1 row + 2 keyed + 1 budget |
| Rollup, one parent | counted 1 + budget 1 + pending 1 | — | counted 3 + budget 1 + pending 2 |
| Tool, one parent | counted 1 + budget 1 | — | counted 3 + budget 1 |
| Writer id | — | WhoAmI once per process (cached, `OntologyWriterDataverseClient.cs:224-241`) | same |

With 1 invoice or 50 invoices on a core record, every path makes the same number of reads (no `In` over invoice ids; paging only beyond 5000
rows). **Re-read under (a):** the link and `not-in` sit inside all three reads, so `SameSums` (`:365-366`) still compares like with like. An
invoice whose approve record appears between the reads leaves or enters the billed part; that changes the billed re-read and triggers the retry.
**Under (b)** the re-read check is not needed (§7.3).

### 7.8 Guards (O3, O4, O5) and parity (O6)

- **O3 `DerivedBaseColumns`** (`:123-136`) gains `sprk_decisionrecord`: `sprk_regardinginvoice`, `sprk_actioncode`, `sprk_steps`, `createdby` and
  `createdonbehalfby` (the last two listed for completeness). It also gains `sprk_invoiceapproval.sprk_invoice`. `sprk_steps` and the portion
  lookup go beyond O3's list, but the key needs them (Q4). All are unsecured in the solution source: only `sprk_createdbyperson` is secured
  (`sprk_DecisionRecord/Entity.xml:795-806`, `sprk_invoiceapproval/Entity.xml:685-696`). The two guards (`DerivedBaseColumnsFieldSecurityTests`,
  `SignalPredicateTests:194-208`) and the pin (`TheDerivedBaseColumns_CoverTheAmountRuleAndTheHeldAntiJoin`) take them.
- **O3 role ArchTest.** It pins `prvReadsprk_DecisionRecord` at Global in `Roles/Spaarke Ontology Service.xml:51`. Must-fire: a scratch copy at
  Deep. Must-not-fire: the real file. The open point: on dev the reading identity's Decision Record read does not come from a
  SpaarkeMaster role (Q3; identity details sent to ontology-r1 and uac-r2 privately). So the XML pin alone does not cover the evaluator. Proposed: a live check that the
  reading identity holds Read on `sprk_decisionrecord` at Global (`RetrieveUserPrivileges` for WhoAmI on the `IDataverseService` connection).
- **O4 hold sources.** `sprk_invoicesubmission.sprk_findingsjson` is secured. It is readable through "Spaarke BFF-Managed Field Writers"
  (`FieldSecurityProfiles.xml:855`, grant `:1457-1464`, CanRead 4); its members on dev include the BFF application identities. `sprk_invoice.sprk_firmhold`
  (`:1625-1631`; Readers profile `:773-779`) is not read by `InvoiceBlockedHold` (Q5).
  - The hold sources do not join `DerivedBaseColumns`. A new list, `FinanceBaseValues.HoldSourceColumns`, carries the pair "secured, readable by
    the BFF application user through Spaarke BFF-Managed Field Writers".
  - The guard, offline: the profile XML grants CanRead 4 on each listed column, and each listed column is secured.
  - The guard, live (dev, `SignalPredicateTests` style): the reading identity is a member of the profile (`systemuserprofiles`).
- **O5:** the in-read canary in §7.6.
- **O6.** One fixture store answers the three reads and the hold read. The same fixture goes through `FinanceBaseValues.ReadAsync`,
  `FinanceRollupService` (its reads move to the `IDataverseService` as `IGenericEntityService`, with no `ServiceClient` unwrap for invoices)
  and the tool. Counted spend must be equal across the three; pending must be equal between `ReadPendingSpendAsync` and the rollup.

### 7.9 Dev probe (read-only, GET and SQL only, 2026-11-08; ids to the lead only)

- **The `exists` link in the grouped aggregate** (the option (a) shape, with the helper's two conditions) is accepted.
  - Counted spend per parent is identical with and without the link: 2 parents, approved part only, billed part empty.
  - With an unknown `createdby` the link returns no rows.
  - Dev has one approve record per counted invoice, so dev data cannot show S5; `exists` is a semi-join, and S5 is tested offline.
- **(b)'s three reads, parent-scoped.** 2 parents, 2 counted invoice rows, 2 portions, 2 writer-created approve records. Both match under the key,
  derived in the probe the same way the C# code derives it. Counted spend on dev is unchanged.
- **Pending.** 2 In Review invoices, each with a linked submission. The held result is part of the implementation's before/after comparison.
- **Identities.** The role and field-security profile memberships of the reading and writing identities were read; the details went to
  ontology-r1 and uac-r2 privately, not into this repository.

### 7.10 Test shape

Each case runs on every reader in scope (evaluator `ReadAsync`, rollup, tool; pending: `ReadPendingSpendAsync` and the rollup):

| Case | Must fire | Control |
|---|---|---|
| S1 | Approved, no writer-created approve record: not counted | same invoice with the record: counted |
| S2 | record by another identity; record on the writer's behalf: not counted | the writer's own record: counted |
| Key | writer-created approve record under another key (another invoice's, unrelated, no or unreadable `sprk_steps`): not counted; no portion: not counted | the approval's own key: counted |
| D-211 | — | Approved invoice with a reviewer-added line (approved > billed) and its key: counted at the approved total; the add-line record is not taken for an approve record |
| S3 | held In Review invoice (recorded outcome Blocked, which the column state cannot show; column state; own submission unlinked): not in pending | released by an Override stamp: in pending |
| O5 | linked own submission whose findings read back empty: pending read throws | readable findings: a value |
| S4 | fault on the row, portion, record or submission read, or on the writer id; `Guid.Empty`: evaluator throws (policy unknown), rollup counted write not made and pending unchanged, tool returns an error | — |
| S5 | two approve records under the key: counted once | — |
| O6 | parity: the same fixture gives equal spend through all three readers | — |
| Count | 1 invoice and 50 invoices: equal read counts (4 per chunk; pending 2) | — |

**Seeds.** (1) Matcher returns every invoice with a writer record (key dropped): the Key must-fire cases fail. (2) Writer conditions dropped: S2
fails. (3) Hold exclusion removed from pending: S3 fails. (4) Canary removed: O5 fails. Each file restored byte-identical (`cmp`).

**Existing tests that change.** In `SpendHeldAndPendingTests`, the store gains inner-link support, and the pins "counted has no link"
(`:291-295`) and the anti-join shape (`:403-488`) are rewritten. `FinanceBaseValuesTests` (grouped builders) and `InvoiceRejectReturnServiceTests:121-125`
change under (b). The `FinanceBaseValuesSeamTests` grouped cases become row-read cases.

### 7.11 Hunks for ontology-r1's review (option (b))

- **`FinanceBaseValues.cs`**
  - H1: `ReadAsync` gains a `Guid writerUserId` parameter, and its spend half sums `ReadCountedInvoicesAsync` per parent (`:262-285`).
  - H2: new `ReadCountedInvoicesAsync` (row read plus `InvoiceApproveRecord.MatchedInvoicesAsync`, parent scope).
  - H3: `ReadPendingSpendAsync` becomes a row read plus `InvoiceBlockedHold`'s batch, with the canary (`:287-312`).
  - H4: `DerivedBaseColumns` additions and the new `HoldSourceColumns` (`:112-136`).
  - H5: retired: `SpendAmountPart` and `SpendAmountPartsInReadOrder` (`:89-110`); `ReadSpendPartsAsync`, `ReadPartAsync` and `SameSums`
    (`:317-366`); the two spend builders (`:368-382`); the part and held branches of `BuildGroupedFetchXml` (`:384-432`, which keeps the budget
    form); `HeldAlias` (`:189`). The remarks at `:25-29` and `:174` are updated.
- **`Services/Signals`**
  - `SignalEvaluator.cs`: the writer id is obtained and passed to `ReadAsync` (`:131-138`, `:233-234`; Q2).
  - `PolicyVersionValidator.cs:150-151`: the remark names the new reader.
  - No change to `WriterCreatedRecord`.
- **ebilling**
  - New `InvoiceApproveRecord.cs`.
  - `InvoiceApprovalService.cs:262,275,1354-1373,1403-1474`.
  - `InvoiceBlockedHold.cs`: the batch reader.
  - `FinanceRollupService.cs:168-244,297-374`, and its constructor.
  - `Tools/FinancialCalculationToolHandler.cs:131-205`, and its constructor.
- **Tests:** §7.10, plus the O3 role ArchTest and the O4 profile guard.

### Questions for ontology-r1

1. Option (b) (recommended), or option (a) with its smaller `FinanceBaseValues` hunk? Is retiring the grouped spend path (H5) acceptable?
2. Writer id into `SignalEvaluator`: inject `OntologyWriterDataverseClient` (already registered, `SignalsModule.cs:51-53`), or should
   `SignalWriter` expose it?
3. O3: the evaluator reads as the BFF application user, whose Decision Record read on dev does not come from a SpaarkeMaster role. Is the role-XML pin
   plus the live `RetrieveUserPrivileges` check enough, or should provisioning (H10) also give the BFF app user Spaarke Ontology Service?
4. Do you agree to add `sprk_steps` and `sprk_invoiceapproval.sprk_invoice` to `DerivedBaseColumns` (the key needs them)?
5. O4 names `sprk_firmhold`, but `InvoiceBlockedHold` does not read it. Should pending also leave out `InvoiceFirmHold`'s hold, or only
   `InvoiceBlockedHold`'s (the POML goal)?
6. O5: is the in-read canary enough, or do you also want one explicit membership check per run (1 query)?
7. Should the rollup call `ReadPendingSpendAsync` (it has no production caller today), so there is one pending reader? Do you accept that an
   In Review invoice with no linked own submission leaves pending (EB-D-201)?

### Questions for uac-r2

1. Does option (b) satisfy your F1 for spend? An invoice counts only when it is in the matched set, and the matcher is the one approval, AP export
   and Mark Paid use. Does option (a) also satisfy it, given the window in which an invoice is counted on the unkeyed link alone?
2. 165b-3's K2 (any-portion match at invoice level) carries into spend unchanged. Do you accept the same limit there?
3. The spend reads find the records through an inner link from `sprk_regardinginvoice` to the parent's invoices. Matching is still by invoice id,
   and the key and the writer conditions are unchanged. Do you have any concern with this parent scope?

### 7.12 uac-r2 answers (2026-11-08)

1. **Option (b) satisfies the keyed rule for spend**, because the keyed matcher decides. Option (a) is accepted only if the keyed step cannot be skipped by construction: the sum takes only the matcher's output type, a sealed type with a private constructor that only the matcher creates (the pattern of field-mapping's `GrantedAssignment`). A test alone is not enough. Prefer (b) unless the row read is measurably too slow.
2. **165b-3 K2 carries into spend: accepted.** Each key binds the invoice id and the portion, so there is no cross-invoice credit. Approve's single-portion refusal still gates approval.
3. **Parent-scoped reads: accepted.** The parent scope only narrows the candidates. Matching stays by invoice id, key and writer conditions.
   - **K (new): attribution follows the invoice's current core lookup.** A legitimate re-file (D-96) moves the invoice's spend with it, and so would an edit to that lookup made outside the BFF. That changes where spend is attributed, not whether it counts: an invoice without its keyed approve record still never counts. The existing guarded-lookup rules cover it, so it is not new risk from 165b-4.
4. **Reading identity: both.**
   - Now: the live privilege check beside the role-XML pin. It fails closed or alerts when the Decision Record read privilege is missing, and it must not pass only because a broad administrator role is present.
   - Durable: least privilege, meaning the BFF identity gets the Decision Record read through a SpaarkeMaster role. That is a per-environment setup step for provisioning and the deployment guide, filed as DEF-059.

### 7.13 ontology-r1 answers (2026-11-08): option (b) approved, code may start

1. **(b), and retiring the grouped spend path (H5) is fine.** Conditions:
   - (i) `SpendAmount` coalesces on null only: the approved total when it is non-null (0 counts as 0), else the total amount. This is the same as the two grouped parts. D-211 stays exact. Fixture: approved total = 0.
   - (ii) The code states that read 1 is the snapshot. Reads 2 and 3 only filter it (an allow-list can remove an invoice, never add one). K1: an invoice approved after read 1 is left out until the next run.
   - (iii) Before `ReadSpendPartsAsync` is deleted, run a dev before/after comparison of counted spend per parent, old path against new, for every parent with counted invoices. Counts go in the PR, no ids.
   - (iv) Both the O6 parity test and the count test (1 invoice and 50 invoices give equal read counts) are required.
2. **Inject `OntologyWriterDataverseClient` into `SignalEvaluator`,** as `DecisionCommitService`, `SignalSuppressionService` and `GateDecisionRecordSharing` already do. `SignalWriter`'s surface stays as it is. Fetch the writer id only when the policy reads spend. A fault or `Guid.Empty` makes that policy unknown for this run (S4). A policy that doesn't read spend is never made unknown by it.
3. **The pin plus a live dev test, with no per-run runtime query.** The test is in the `SignalPredicateTests` style: `RetrieveUserPrivileges` for the reading identity (WhoAmI) asserts Read on `sprk_decisionrecord` at **Global** depth. A lower depth fails silently (matched records go missing and spend is undercounted), whereas a missing privilege throws. The test names no role or identity. The Ontology Service XML pin stays. ontology-r1 does not want provisioning to grant the role; uac-r2 does (§7.12 item 4). That is an owner decision, tracked on DEF-059.
4. **Yes:** `sprk_steps`, `sprk_regardinginvoice`, `sprk_actioncode` and `sprk_invoiceapproval.sprk_invoice` join `DerivedBaseColumns` under both guards. All are unsecured in the solution source.
5. **Only `InvoiceBlockedHold`'s hold leaves pending.** A firm hold resolves to spend once a firm is assigned, so it stays in pending. `sprk_firmhold` is not read, and O4's text no longer names it.
6. **The canary is enough.** Precondition before building: a read-only dev count of linked own submissions whose `sprk_findingsjson` is null, legacy rows included, must be 0. If it is not 0, stop and tell ontology-r1. The live profile-membership guard stays as a dev test.
7. **Yes:** the rollup calls `ReadPendingSpendAsync`, so there is one pending reader. EB-D-201 is accepted. The reader logs the count of In Review invoices it leaves out as unlinked; the count is in memory, so it costs no extra query.

Review: the `FinanceBaseValues` and `Services/Signals` hunks go through ontology-r1's review on the PR. One independent review per change set, then a re-check of the fixes.

## 8. 165b-4 implementation

Option (b) as §7 describes it, with §7.12 and §7.13 applied. Every spend reader decides only from server-written sources: counted spend keeps
an invoice only when the writer's approve Decision Record exists under one of the invoice's own approval keys, and pending spend leaves out the
invoices `InvoiceBlockedHold` holds.

### 8.1 What changed

- **One keyed matcher** (`Services/Finance/InvoiceApproveRecord.cs`, new static class). It has two scopes over one rule (`Keys` + `Matched`, pure):
  invoice-id scope (approval, AP export, Mark Paid, through `InvoiceApprovalService.InvoicesWithApproveRecordAsync`, which now delegates with no
  copy) and parent scope (the three spend readers). The writer conditions come from `WriterCreatedRecord` (Services/Signals); no copy.
- **Counted spend.** `FinanceBaseValues.ReadCountedInvoicesAsync` is the one counted reader: read 1 (paged row read, `InvoiceRowQuery`) is the
  snapshot; reads 2 (portions) and 3 (approve records), parent-scoped with an inner link to the invoice, only filter it. `ReadAsync` (evaluator),
  `FinanceRollupService` and `FinancialCalculationToolHandler` all sum it with `SpendAmount` per row (null-only coalesce; 0 counts as 0).
- **Pending spend.** `ReadPendingSpendAsync` is the one pending reader (the rollup now calls it): a paged In Review row read, then
  `InvoiceBlockedHold.CheckBatchAsync` (one paged submission read per chunk, inner link to the parent's In Review invoices, rule applied in memory).
  Only `InvoiceBlockedHold`'s hold leaves pending; `sprk_firmhold` is not read. The reader logs (Warning) the count of In Review invoices it leaves
  out because they have no linked own submission (EB-D-201), from memory.
- **O5 canary** is in the batch read: an own linked submission whose findings read back empty throws, so the pending figure is unknown, never
  "not held".
- **Writer id.** `SignalEvaluator` takes `OntologyWriterDataverseClient` by injection and fetches the id only when the policy reads spend and has
  candidates; a fault fails that policy (unknown, S4). `ReadAsync` and `ReadCountedInvoicesAsync` refuse `Guid.Empty` before any read. The rollup
  and the tool get it from `BillingDecisionRecorder.WriterUserIdAsync` (the same client).
- **Rollup and tool reads** now go through `IGenericEntityService` (the same app-only connection); the `ServiceClient` unwrap is gone, which is
  what makes the three readers testable on one fixture.
- **Retired** (H5): `SpendAmountPart`, `SpendAmountPartsInReadOrder`, `ReadSpendPartsAsync`, `ReadPartAsync`, `SameSums`, the grouped spend and
  pending builders and the part/held branches of the generic builder (the budget builder stays), `HeldAlias`, the rollup's `BuildInvoiceQuery`,
  `BuildPendingInvoiceQuery` and `QueryPendingInvoicesAsync`, and the tool's `BuildInvoiceQuery`.

### 8.2 K1 (ontology-r1 condition ii)

Read 1 is the snapshot. Reads 2 and 3 are an allow-list over it: they can remove an invoice, never add one. An invoice approved after read 1 is
left out until the next run. The code says so in `ReadCountedInvoicesAsync`'s remarks.

### 8.3 Reads per call (O7)

Per chunk of 200 parents: counted = 1 paged row read + 1 portion read + 1 record read (2 and 3 skipped when the snapshot is empty); budget = 1
grouped aggregate; pending = 1 paged row read + 1 submission read. The evaluator's spend policy therefore sends 4 reads per chunk (was 3 invoice +
1 budget). The count test pins it: 1 invoice and 50 invoices give the same reads (4 counted-and-budget, 2 pending).

### 8.4 Hunks for ontology-r1's review (line numbers in the new file)

- **`FinanceBaseValues.cs`**
  - Class remarks `:27-28` (counted spend needs the approve record).
  - `SpendAmount` doc `:76-77`; `SpendAmountPart` and `SpendAmountPartsInReadOrder` removed (old `:89-111`).
  - `DerivedBaseColumns` `:96-120` (doc rewritten; six entries added: `sprk_decisionrecord.sprk_regardinginvoice`, `sprk_actioncode`,
    `sprk_steps`, `createdby`, `createdonbehalfby`, `sprk_invoiceapproval.sprk_invoice`).
  - New `HoldSourceColumns` `:125-128` (forwards to `InvoiceBlockedHold.HoldSourceColumns`; the C1 findings ArchTest allows the column
    name only in reviewed types, so the list lives there).
  - `HeldSubmissionConditions` doc `:158-170`; `HeldAlias` removed.
  - `ReadAsync` `:244-290` (`writerUserId` parameter, `Guid.Empty` refused, spend from `ReadCountedInvoicesAsync`).
  - `ReadPendingSpendAsync` `:292-352` (row read, hold batch, unlinked count log).
  - New `ReadCountedInvoicesAsync` `:355-405`, `InvoiceRowQuery` `:409-425`, `Distinct` `:427-432`, `SumSpendByParent` `:434-451`.
  - `BuildGroupedBudgetFetchXml` `:453-470` (budget only); the grouped spend path removed (old `:317-432`).
- **`Services/Signals`**
  - `SignalEvaluator.cs`: constructor and field `:129-142`; `EvaluateAsync` `:232-246` (writer id fetched only when the policy reads spend and has
    candidates, passed to `ReadAsync`).
  - `PolicyVersionValidator.cs:150-151`: the remark names `ReadCountedInvoicesAsync`.
  - No change to `WriterCreatedRecord`. No DI change (`SignalEvaluator` is a singleton; the client is already registered).
- **ebilling:** `InvoiceApproveRecord.cs` (new); `InvoiceApprovalService.cs:1402-1407` (only `InvoicesWithApproveRecordAsync`'s body and doc);
  `InvoiceBlockedHold.cs:101-170` (`HoldSourceColumns`, `CheckBatchAsync`); `FinanceRollupService.cs` and `Tools/FinancialCalculationToolHandler.cs`
  (constructors take `BillingDecisionRecorder`; reads via the shared readers).

### 8.5 Precondition and dev comparison (read-only, as the operator's CLI identity; ids to the lead only)

- Precondition: linked own submissions with a null `sprk_findingsjson`, legacy rows included: **0** (6 linked submissions, all own, all readable).
- Counted spend, old grouped path against the new path, per parent with counted invoices: `sprk_matter` 2 parents, 2 equal, 0 differ (2 counted
  invoices, both kept); `sprk_project` 0 parents. Pending: `sprk_matter` 2 parents, 2 equal, 0 differ; `sprk_project` 0 parents. No difference,
  so `ReadSpendPartsAsync` was deleted.

### 8.6 Tests

- `SpendServerWrittenSourcesTests` (new, over `SpendWorld`, an in-memory store answering the real queries) runs every case through the evaluator's
  reader, the rollup and the tool: S1, S2 (another user, on behalf of the writer), key mismatch (another invoice's key, unrelated key, no key, no
  portion), D-211 (approved total 0 counts 0; a reviewer-added line's record is not an approve record and is not needed), S5, S3 (three hold
  shapes on both pending readers, with the override control), the unlinked log, O5, S4 (a fault on the invoice, portion, record or writer-id read
  fails the read and counts nothing; an unknown writer is refused before any read), O6 parity, and the count test.
- `SignalReevaluationJobTests` (evaluator level, ontology-r1 §7.13 item 2): a WhoAmI fault makes a spend policy a policy failure with no
  spend read and nothing raised; control, a budget-only policy under the same fault raises its Signal. The read-count assertions there and in
  `SignalEvaluationTriggerHandlerTests` now expect 1 budget aggregate plus the row, portion and record reads.
- `SpendHeldAndPendingTests`: K1 column collector over the real reads (every column is a derived base column or a listed hold source), with its
  control.
- ArchTest `SpendReaderSourceAccessGuardTests`: O3 (the reading role's Decision Record read is Global; must-fire Deep, Local, Basic, missing,
  look-alike) and O4 (each hold source is secured and the profile grants read; controls).
- Live (opt-in, `SIGNALS_LIVE_DATAVERSE_URL`): `SpendReaderSourceAccessLiveTests` (O3 read depth and O4 profile membership for the WhoAmI
  identity; it names no role and no identity) and the rewritten `FinanceBaseValuesSeamTests` (row reads, matcher and hold batch accepted by
  Dataverse). Not run in this task.
- Seeds (each file restored byte-identical; run over the 61 tests of `SpendServerWrittenSourcesTests`, `SpendHeldAndPendingTests` and the
  two evaluator tests, all passing unseeded):
  1. Drop the key match (`InvoiceApproveRecord.Matched` ignores the key): 12 fail.
  2. Drop the hold exclusion (`ReadPendingSpendAsync` keeps held rows): 8 fail, among them the unlinked-log test and O6 parity.
  3. Drop the canary (`CheckBatchAsync` never throws on unreadable findings): 2 fail.
  4. Drop the writer conditions (`RecordQuery` without `WriterCreatedRecord`): 4 fail, among them O6 parity.
  5. Ask for the writer id for every policy (`SignalEvaluator` without `needSpend &&`): 1 fails (the budget-only control).
  (Failing theory cases were counted, not listed by name.)

### 8.7 Known limits

- K1 above.
- 165b-3 K2 (any-portion match) carries into spend (uac-r2 §7.12 item 2).
- uac-r2 §7.12 item 4 asks that the live privilege check not pass only because a broad administrator role is present. The live O3 test reads the
  effective depth, so a broad role satisfies it; the least-privilege grant is the owner decision tracked on DEF-059.

### 8.8 Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- Filtered unit tests (Finance | Signals | ApExport | Approval | MarkPaid | Rollup): 11201 total, 11193 passed, 8 skipped, 0 failed.
- `dotnet test tests/Spaarke.ArchTests/`: 2189 passed, 0 failed (the D-130 tool guard now also requires the shared counted reader).
- No package added or changed.
