# ISS-031 (#2882): a blocked Override or Return refuses while the other verb's Decision Record exists

> **Task:** `tasks/iss031-blocked-other-decision-guard.poml` (FULL) · **Base:** `task/ebilling-iss031` from 0890908f68 (`origin/work/ebilling-r1`) · **Status:** built and tested. Code-review, adr-check and the adversarial pass are for the coordinator.

## The defect

An Override writes its Decision Record and closes the open Signals as Acted before it releases the hold. When the release fails (503, or 409 `concurrent_change` at the bound), the invoice stays In Review and the submission stays Blocked. A Return looked up a stored record only under its own decision id, found none, read the open Signals (none: the Override closed them) and went ahead. The invoice ended Returned to Firm while carrying an Override record. The reverse order (a Return stranded before its transition, then an Override) had the same gap.

## What was built

| Item | Where |
|---|---|
| `BlockedSubmissionErrors.OtherDecisionPending = "ebilling.blocked.other_decision_pending"` (409) and `OtherDecisionPendingDetail(bool pendingIsOverride)` | `Services/Finance/SubmissionOverrideService.cs` (the errors class lives at the top of this file) |
| The guard: `DecideLockedAsync` looks up the OTHER verb's writer-created record through the same `_findStored` (the other verb's action code and decision id: `OverrideActionCode` + `OverrideDecisionIdFor`, or `ReturnActionCode` + `ReturnDecisionIdFor`). When it exists: 409 `other_decision_pending`, nothing written. A `DecisionRecordRefusedException` goes through `RecordRefusal` and any other fault answers `Unavailable` (503), exactly as the same verb's lookup does | `SubmissionOverrideService.DecideLockedAsync`, immediately after `AuthorizeCloseAsync` |
| Class remarks ("Order and retries") and the endpoint remarks name the new 409 | `SubmissionOverrideService.cs`, `Api/Finance/SubmissionOverrideEndpoints.cs` |

The detail names the pending decision and tells the caller to repeat it:
- pending Override: "An override of this submission is recorded but has not finished, so it cannot also be returned. Repeat the override to finish it."
- pending Return: "A return of this submission to the firm is recorded but has not finished, so it cannot also be overridden. Repeat the return to finish it."

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

The Invoice Review client (`src/solutions/InvoiceReview/src/services/decisionApi.ts`) does not map `ebilling.blocked.other_decision_pending` yet. A reviewer who reaches it sees the client's generic failure message. On the page, the 095f-2ui client guard already blocks switching verbs while it holds the page lock; the server code covers a reload, another tab or another reviewer. A later client task should map the code and offer "Finish the pending decision" (it can name the verb from the detail or from the code plus the page state).

## Known limits

- **Both records already exist (pre-fix data only).** If the defect already ran to a point where both verbs' records exist and the submission is still held, both verbs are now refused 409 `other_decision_pending` and the submission cannot be decided from the routes; it needs an operator to resolve. Not reachable once this check is deployed. Where the pre-fix double decision ran to completion, the submission is already released and the routes answer by state as before.
- **A Return caller with no Signals to close is not asked for rights.** As before this fix, `AuthorizeCloseAsync` asks rights only when the decision closes Signals; with none open (a stranded Override closed them), a Return caller reaches the 409. The route's own filter still requires Read and Write on the submission. This is the existing 095f-2c behaviour for the same verb's 409 `decision_conflict`.
- **Lock expiry.** The guard relies on the per-invoice decision lock to serialise the two verbs, as every other check in this method does.
