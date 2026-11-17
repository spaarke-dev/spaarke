# ISS-031 (#2882): a blocked Override or Return refuses while the other verb's Decision Record exists

> **Task:** `tasks/iss031-blocked-other-decision-guard.poml` (FULL) · **Base:** `task/ebilling-iss031` from 0890908f68 (`origin/work/ebilling-r1`) · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

## The defect

An Override writes its Decision Record and closes the open Signals as Acted before it releases the hold. When the release fails (503, or 409 `concurrent_change` at the bound), the invoice stays In Review and the submission stays Blocked. A Return looked up a stored record only under its own decision id, found none, read the open Signals (none: the Override closed them) and went ahead. The invoice ended Returned to Firm while carrying an Override record. The reverse order (a Return stranded before its transition, then an Override) had the same gap.

## What was built

| Item | Where |
|---|---|
| `BlockedSubmissionErrors.OtherDecisionPending = "ebilling.blocked.other_decision_pending"` (409) and `OtherDecisionPendingDetail(bool pendingIsOverride, bool callerIsDecider)` (the second parameter added in review round 1) | `Services/Finance/SubmissionOverrideService.cs` (the errors class lives at the top of this file) |
| The guard: `DecideLockedAsync` looks up the OTHER verb's writer-created record through the same `_findStored` (the other verb's action code and decision id: `OverrideActionCode` + `OverrideDecisionIdFor`, or `ReturnActionCode` + `ReturnDecisionIdFor`). When it exists: 409 `other_decision_pending`, nothing written. A `DecisionRecordRefusedException` goes through `RecordRefusal` and any other fault answers `Unavailable` (503), exactly as the same verb's lookup does | `SubmissionOverrideService.DecideLockedAsync`, immediately after `AuthorizeCloseAsync` |
| Class remarks ("Order and retries") and the endpoint remarks name the new 409 | `SubmissionOverrideService.cs`, `Api/Finance/SubmissionOverrideEndpoints.cs` |

The detail names the pending decision and says who finishes it (review round 1, F1: only the stored decider can, so "repeat it" is said only to them; chosen by `otherRecorded.ConfirmedByUserId == decider`):
- pending Override, caller is its decider: "An override of this submission is recorded but has not finished, so it cannot also be returned. Repeat the override to finish it."
- pending Override, anyone else: "Another person's override of this submission has not finished, so it cannot also be returned. They complete it by repeating it."
- pending Return, caller is its decider: "A return of this submission to the firm is recorded but has not finished, so it cannot also be overridden. Repeat the return to finish it."
- pending Return, anyone else: "Another person's return of this submission to the firm has not finished, so it cannot also be overridden. They complete it by repeating it."

## Placement and order (why)

The order inside the lock is now: re-read, `Refusal` by the submission's disposition, the invoice not-held check, the stored findings, the same verb's `_findStored`, the Signal set read, **the rights (`AuthorizeCloseAsync`, 403)**, **the other verb's lookup (409 / 503)**, the same verb's stored-decider (409 `decision_conflict`) and readability (503) checks, then the firm gate and everything else. The Override privilege (403 `not_permitted`) and a Return's unverified decider (403) are checked before any read, as before.

1. **After the rights.** A caller without the rights gets 403, not a 409 that tells them a decision is pending (the 095f-2c re-check rule for the same verb's stored decider). The lookup also runs after the rights, so a caller without them gets 403 even when the lookup would fault (tested).
2. **After the not-held checks.** A finished other decision never reaches the guard, and needs no extra read:
   - An Override whose release finished has set the submission Overridden: a Return is refused `already_overridden` by `Refusal`.
   - A Return finished to Returned to Firm (even with its submission write still to do, the `return-state-write` strand) is refused by the invoice check: `already_returned`.
   - So the guard only fires in the stranded states it exists for: the submission still Blocked, the invoice still In Review, and the other verb's record written.
3. **Before the same verb's stored record is used.** If both records ever exist, neither decision is finished from them (see known limits). A record of the same verb never makes the guard skip.
4. **Placement relative to the same-verb lookup.** The other lookup is a separate read placed after the rights (not next to the same-verb read before them) for reason 1. The cost is one extra Decision Record lookup per decision that reaches this point.

## The same verb's repeat is unchanged

With no record of the other verb (the only state reachable once this check exists), the same verb's repeat finishes from its stored record exactly as 095f-2c built it. The golden hashes in `SubmissionOverrideDecisionNoteTests` (`174D4566...`, `779A8F87...`, `10BFFB69...`) are unchanged and pass.

## The repair path (`RecloseAsync`, about line 1960; and `RepairReleasedColumnsAsync`)

Not changed, by decision. Both run only when `Refusal` answers AlreadyDecided, which means the submission is already in THIS verb's released state (`Overridden` for an Override repeat, `Returned` for a Return repeat). `RecloseAsync` re-closes the Signals of THIS verb's stored set with THIS verb's record and decider; `RepairReleasedColumnsAsync` rewrites only this verb's own released columns. Neither writes a Decision Record, moves the invoice, or releases a hold, so neither can create a second decision. Once this guard exists, the other verb's record cannot be written while this verb is stranded, and this verb cannot finish while the other's record exists, so the repair path cannot meet the other verb's record in a state the guard allows. Refusing the re-close when an other-verb record exists (a pre-fix state) would leave this decision's Signals Open forever with nothing else to close them, which is worse.

## Tests

New file `tests/integration/data-mutation/Ebilling/SubmissionOverrideOtherDecisionTests.cs` (22 cases), over the real service and the real Decision Record writer harness. `SubmissionOverrideDecisionNoteTests.StrandAsync` became `internal` so the strand worlds are reused, and the test `World` gained `FindFaultFor` (a lookup fault for one decision id only).

| Acceptance criterion | Test | Fail-before |
|---|---|---|
| A stranded Override (release failed; closes and release failed; invoice bound exhausted) then a Return: 409 `other_decision_pending`, detail names the Override; nothing written (records, set stores, closes, release transactions, transitions, submission writes, notices, findings, Signal statuses, the decision note column all unchanged). The Override's repeat then finishes; the Return is then refused `already_overridden` | `StrandedDecision_RefusesTheOtherVerb_AndWritesNothing` (`override-release`, `override-closes-and-release`, `override-invoice-bound`) | 3 of 3 fail |
| The same for a stranded Return (its transition failed, its Signals closed) then an Override; the Override is then refused `already_returned` | `StrandedDecision_RefusesTheOtherVerb_AndWritesNothing` (`return-transition`) | fails |
| A Return stranded AFTER its transition: the Override is refused `already_returned` by state, nothing written | `ReturnStrandedAfterItsTransition_RefusesAnOverride_ByState` | passes (control: pins the not-held order) |
| Must-not-fire: neither record exists | `NoRecordOfEitherVerb_DecidesAsBefore` (2) | passes (control) |
| Must-not-fire: the other verb failed BEFORE its record (no record) | `OtherVerbFailedBeforeItsRecord_DoesNotBlock` (2) | passes (control) |
| Must-not-fire: the same verb's repeat finishes from its stored record | `SameVerbRepeat_FinishesAsBefore` (4 strands), plus the unchanged 095f-2c strand and golden-hash tests | passes (control) |
| A fault on the other verb's lookup: 503 (`unavailable` for a raw fault, `record_unavailable` for the writer's lookup-failed refusal, as the same verb's lookup answers), nothing written, Signals still Open; healthy, the decision goes ahead | `AFaultOnTheOtherVerbsLookup_Is503_AndWritesNothing` (4) | 4 of 4 fail |
| The same after a strand: 503, the stranded state unchanged | `AFaultOnTheOtherVerbsLookup_AfterAStrand_Is503_AndWritesNothing` (2) | 2 of 2 fail |
| 403 first: a stranded Override with its Signals still open, then a Return by a caller without Write: 403 `not_authorized` | `StrandedOverride_ReturnWithoutRights_Is403First` | passes (control) |
| 403 first: a stranded Return with its Signals still open, then an Override without Write (403 `not_authorized`) or without the privilege (403 `not_permitted`); with both, 409 `other_decision_pending` | `StrandedReturn_OverrideWithoutRights_Is403First` | fails (on the final 409) |
| 403 first even when the other lookup would fault | `NoRights_Is403_EvenWhenTheOtherLookupWouldFault` | passes (control) |

**Fail-before run.** The guard was disabled in the source (the lookup skipped, the constants kept so the tests compile), the new tests were run, and the file was restored: **11 failed, 11 passed** of 22. The 11 that pass before are the must-not-fire and ordering controls listed above; they pin behaviour that must not change.

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | Build succeeded, 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~SubmissionOverride\|FullyQualifiedName~DecisionNote\|FullyQualifiedName~DecisionRecord\|FullyQualifiedName~BlockedSubmission"` | Passed: 1295, Failed: 0, Skipped: 1 (1296) |
| `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~Invoice\|FullyQualifiedName~Decision"` | Passed: 147, Failed: 0 |

No package, project file or DI registration changed (no publish-size delta to measure beyond a few hundred bytes of IL).

## Component justification (CLAUDE.md section 11)

New surface: one error code and its detail sentence, and one extra read in an existing method. (1) Existing: `_findStored` (`BillingDecisionRecorder.FindStoredResolvedSetAsync`), the decision ids and action codes, `RecordRefusal` and `Unavailable`; all reused. (2) Extension: the existing lookup is called a second time with the other verb's key; no new seam. (3) Cost of doing nothing: one blocked submission can end with two conflicting decisions (an Override record on a returned invoice, or the reverse), with Signals closed by the losing decision.

## Client (not changed)

The Invoice Review client (`src/solutions/InvoiceReview/src/services/decisionApi.ts`) has no mapping of its own for `ebilling.blocked.other_decision_pending`. Corrected in review round 1: it does NOT show a generic message. `decisionFailureMessage` shows the server's `detail` as is, followed by the code in brackets, so the reviewer reads the server's sentence (which, since round 1, says whether they can finish the pending decision themselves or another person must). `isBlockedDefiniteRefusal` treats the 409 as a definite refusal (it is not one of the transient 409s), so the form's lock is released. On the page, the 095f-2ui client guard already blocks switching verbs while it holds the page lock; the server covers a reload, another tab or another reviewer. A later client task may offer "Finish the pending decision" directly. The invoice's reject and return answer the same code (round 1) through the same `decisionFailureMessage`.

## Known limits

- **Both records already exist (pre-fix data).** If the defect already ran to a point where both verbs' records exist and the submission is still held, both verbs are refused 409 `other_decision_pending` (tested: `BothRecordsStored_BothVerbsAreRefused_AndWriteNothing`) and the submission cannot be decided from the routes. Where the pre-fix double decision ran to completion, the submission is already released and the routes answer by state as before.
- **OPEN OWNER QUESTION: a stranded decision whose decider cannot return (corrected in review round 1).** The first version of these notes said the stuck state is "not reachable once this check is deployed". That is wrong. If decider A strands an Override (record written, release failed) and then leaves, or loses the eBilling Override privilege, nobody can finish it: another person's repeat is refused 409 `decision_conflict` (095f-2c's decider rule), a Return is refused 409 `other_decision_pending` (this guard), and the invoice's own reject and return are refused the same way for a Procedure-held submission (round 1). The invoice stays held. The same holds for a stranded blocked Return whose decider cannot return. The owner must choose: an operator path that finishes or voids a stranded decision, or relaxing the decider rule so that another privileged person can finish it. The coordinator is filing the issue.
- **A Return caller with no Signals to close is not asked for rights.** As before this fix, `AuthorizeCloseAsync` asks rights only when the decision closes Signals; with none open (a stranded Override closed them), a Return caller reaches the 409. The route's own filter still requires Read and Write on the submission. This is the existing 095f-2c behaviour for the same verb's 409 `decision_conflict`.
- **Lock expiry.** The guard relies on the per-invoice decision lock to serialise the two verbs, as every other check in this method does.

## Review round 1

Base `41d0d3b5f5`. Fail-before evidence: the named guard was seeded away in the source, the new tests were run, and the source was restored from a saved copy.

| Finding | Fix | Tests | Fail-before |
|---|---|---|---|
| **F1.** The 409 detail said "Repeat the override/return to finish it" to every caller, but only the stored decider can finish it (095f-2c rule; anyone else is refused `decision_conflict`). | `OtherDecisionPendingDetail(pendingIsOverride, callerIsDecider)`, chosen by `otherRecorded.ConfirmedByUserId == decider`. The decider keeps "Repeat the ... to finish it"; anyone else reads "Another person's override/return of this submission has not finished ... They complete it by repeating it." Same code. | `SubmissionOverrideOtherDecisionTests.StrandedDecision_AnotherPerson_IsToldItIsTheirsToComplete` (override and return strands); the decider's detail stays pinned by `StrandedDecision_RefusesTheOtherVerb_AndWritesNothing` | 2 of 2 fail with the detail forced to the decider's |
| **F1 (found in passing).** The same double decision through the invoice's own route: a Procedure-held submission (SPA: Accepted, so the FR-20 hold does not hold its invoice) can be Overridden; with the release failed, an eligible approver could Reject or Return the invoice through `InvoiceRejectReturnService`, which checked only `InvoiceBlockedHold`. The Override then never releases, and its repeat is refused (`invoice_not_held` after a Reject, `already_returned` after a Return). | `InvoiceRejectReturnService.DecideHeldAsync`, right after the FR-20 hold (moved below `AuthorizeCloseAsync` in round 2): `PendingOverrideAsync` reads the invoice's linked submissions, takes the OWN one (`InvoiceBlockedHold.OwnSubmissionOf`; the only one an Override decides), and only if it is still held for an Override (`SubmissionOverrideService.IsHeldForOverride`: `Classify` is Blocked or ProcedureHeld) looks up the Override record (`FindStoredResolvedSetAsync` with `OverrideActionCode` and `OverrideDecisionIdFor(own)`, the Override's own lookup). Found: 409 `InvoiceRejectReturnCodes.OverridePending` = `ebilling.blocked.other_decision_pending` (the same wire code: the other decision on this held submission has not finished), decider-aware detail, nothing written. A fault on either read: 503 `ebilling.invoice.decision_unavailable`, failed step `override-record`. Reject and Return endpoint remarks list the 409. | `InvoiceRejectReturnOverridePendingTests`: `StrandedOverride_RefusesRejectAndReturn_AndWritesNothing` (2), `StrandedOverride_TheDeciderIsToldToRepeatIt`, `AFaultOnTheOverrideLookup_Is503_AndWritesNothing` (2); must-not-fire `NoOverrideRecord_DecidesAsBefore` (2) and `FinishedOverride_DecidesAsBefore` (2); control `StrandedOverride_ApproveIsRefusedByTheProcedureHold` | 5 of 5 fail with the guard disabled. The must-not-fire tests pass with it disabled (they pin unchanged behaviour); `FinishedOverride_DecidesAsBefore` fails 2 of 2 when the "still held for an Override" condition is removed, so it proves the released Override does not refuse. The approve control passes before and after. |
| **Test gap.** No test with both records stored. | Test only. | `BothRecordsStored_BothVerbsAreRefused_AndWriteNothing`: an Override stranded, the Return record seeded through `w.Recorder`, then Override, Return, Override: each 409 `other_decision_pending`, nothing written. | Fails with the other-verb guard disabled (the Override repeat finishes from its own record). |
| **Notes.** "Not reachable once this check is deployed" was wrong; the client shows the server's detail, not a generic message. | Known limits restated as an open owner question (a decider who cannot return strands the invoice; operator path or relaxed decider rule). Client section corrected. | - | - |

### Which invoice verbs can reach a stranded Override

- **Reject and Return (`InvoiceRejectReturnService`): yes, both, for a Procedure-held submission whose invoice the FR-20 hold does not hold.** An SPA Procedure gap or unresolved hold is Accepted, and `InvoiceBlockedHold` holds only a Blocked outcome without a release stamp, so neither verb was stopped. Both are guarded. For an Internal submission (ordinary block or Internal Procedure gap: Rejected / Blocked) the FR-20 hold already refuses both verbs with `ebilling.invoice.held` until the release lands, so the guard is not reached.
- **Approve (`InvoiceApprovalService`): no.** Approve refuses on the FR-20 hold and on the Procedure hold (`InvoiceProcedureHold.ReasonsAsync`), and a submission is released only by the Override's release transaction, which writes the reason code Overridden and the stamp together (`InvoiceProcedureHold.IsReleased`). A stranded Override has written neither, so approve is refused `procedure_held` (pinned by the control test). Not changed.
- **A finished Override does not refuse.** Once released, the invoice is In Review for its ordinary decision; rejecting or returning it then is the expected next step, not a second decision on the hold.
- **The reverse order** (an invoice reject or return first, then an Override) needs no guard: the invoice decision writes its record after its transition, so once it has a record the invoice is no longer In Review and the Override is refused `invoice_not_held` / `already_returned` by state. Before its transition it has only a claim on the approval portion, which is not a decision.

### Known limit added by this round

- **A Procedure hold re-raised after a finished Override.** `IsHeldForOverride` follows `Classify`. If a later re-run re-holds the submission (dropping the Override stamp) while the Override's record stays, the invoice's reject and return are refused `other_decision_pending` until an Override is repeated, and that repeat finds the stored record and finishes from it (the decision id is per submission). This follows the existing "a re-hold drops the stamp" rule; it is not new behaviour of the Override itself. Round 2 (K2): unreachable today, see below.

### Gates (round 1)

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | Build succeeded, 0 warnings, 0 errors |
| BFF tests, filter `SubmissionOverride\|DecisionNote\|DecisionRecord\|BlockedSubmission\|InvoiceRejectReturn\|InvoiceApproval` | Passed: 1659, Failed: 0, Skipped: 1 (1660) |
| BFF tests, wider filter `Invoice\|Ebilling` (extra check) | Passed: 7922, Failed: 0, Skipped: 3 (7925) |
| ArchTests, filter `Invoice\|Decision` | Passed: 147, Failed: 0 |

### Component justification (round 1 additions)

New surface: `InvoiceRejectReturnCodes.OverridePending` (an alias of the existing wire code) with its detail, the private `PendingOverrideAsync`, and two internal members on `SubmissionOverrideService` (`OverrideStateColumns`, `IsHeldForOverride`). (1) Existing: `InvoiceBlockedHold.OwnSubmissionOf`, `SubmissionOverrideService.Classify`, `BillingDecisionRecorder.FindStoredResolvedSetAsync` and `OverrideDecisionIdFor`. (2) Extension: all reused; `IsHeldForOverride` only exposes `Classify`'s answer so the invoice route does not re-derive "held for an Override" (one classification). (3) Cost of doing nothing: an invoice whose Procedure-hold Override was stranded can be rejected or returned on top of it, leaving an Override record that can never release.

## Review round 2

Base `a5289dbb63`. The re-check found no F1.

| Finding | Fix | Tests | Fail-before |
|---|---|---|---|
| **F2.** `PendingOverrideAsync` re-implemented the linked-submissions query. | It calls `InvoiceBlockedHold.ReadOwnSubmissionAsync(_entities, invoiceId, SubmissionOverrideService.OverrideStateColumns, ct)`. The column set was already an internal member (`OverrideStateColumns`), so nothing new is exposed. | Covered by the existing round-1 tests (unchanged, all pass). | Refactor; no behaviour change. |
| **Order.** On the blocked routes 403 comes before the 409; the invoice route asked for the Override record before the Signal-close rights. | The guard moved from just after the FR-20 hold to just after `AuthorizeCloseAsync`, before the filing check, the claim and every write. Order now: not eligible (403), claimed by another (409), FR-20 hold (409 `held`), firm gate (409 `firm_missing`), the Signals read, the close rights (403 `not_authorized`), then `other_decision_pending`. What else this changes: on a stranded Override, a firm refusal and a Signals read fault now answer before the 409 (both still write nothing); nothing on any path without an Override record changes. All existing reject, return and approval tests pass unchanged. | `StrandedOverride_NoCloseRights_Is403BeforeThe409` (reject and return) | 2 of 2 fail with the guard in its round-1 position (409 instead of 403). |
| **Control (a).** An ineligible caller on a stranded Override. | Test only. | `StrandedOverride_IneligibleCaller_Is403NotEligible` (2): 403 `not_eligible`, no 409, nothing written. | Passes in both positions (eligibility is first either way): pins the order. |
| **Control (b).** An invoice already at the verb's target with an Override record. | Test only. | `InvoiceAtTarget_WithAnOverrideRecord_TheRepeatFinishes` (2): the claimant's decision stranded after its transition (no record), an Override record then present; the claimant's repeat goes through `CompleteOrNoOpAsync` and finishes (one record), and another approver's repeat is AlreadyDecided, never the 409. | Passes in both positions: the guard is not on the repeat path. |

### Known limits recorded in round 2

- **K3: a permanent refusal from the Override lookup becomes a retryable 503.** `PendingOverrideAsync`'s catch maps every fault to 503 `ebilling.invoice.decision_unavailable` (failed step `override-record`), including a `DecisionRecordRefusedException` that will not change on retry (more than one record under the key, or a stored set the writer cannot read). The reject and return replay already treat a lookup refusal the same way. The reviewer can retry forever without success; the operator path for such a record is ISS-034.
- **K2: the re-hold limit (round 1) is unreachable today.** A Procedure hold can only come back after a finished Override through a re-run, and the only re-run that rewrites a released submission's outcome is the one Set firm starts. Set firm on an invoice that already has its firm is refused `firm_already_set`, and an Override needs the firm gate open (the firm already set), so no current path re-holds an overridden submission. If a future writer re-holds one, the round-1 limit applies.

### Gates (round 2)

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | Build succeeded, 0 warnings, 0 errors |
| BFF tests, filter `SubmissionOverride\|DecisionNote\|DecisionRecord\|BlockedSubmission\|InvoiceRejectReturn\|InvoiceApproval` | Passed: 1665, Failed: 0, Skipped: 1 (1666) |
| ArchTests, filter `Invoice\|Decision` | Passed: 147, Failed: 0 |
