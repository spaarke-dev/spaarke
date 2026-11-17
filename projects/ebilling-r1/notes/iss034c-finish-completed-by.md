# ISS-034 (#2888), task iss034c: the blocked-decision stamp records who completed the decision

> **Task:** `tasks/iss034c-finish-completed-by.poml` (FULL) · **Decision:** EB-D-253 (owner) · **Base:** `task/ebilling-iss034c` from 8b047c19f1 (`work/ebilling-r1`) · **Fixes:** iss034a K5.
> Code-review, adr-check and the adversarial pass are for the coordinator.

## What changed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Finance/SubmissionOverrideService.cs` | `BlockedDecisionStamp` gains `Guid? CompletedBySystemUserId = null` (a defaulted last member, so every existing construction compiles). `WithStamp` writes `completedBySystemUserId` (`"D"` Guid) only for a stamp that names one. `ReadStamp` reads it: a stamp without the member, or one whose member is not a Guid or is empty, reads as `null` ("not recorded"), never a fault, and is still a valid release stamp (`InvoiceBlockedHold.IsValidStamp`). `DecideLockedAsync` passes the completer at the two sites that build the stamp it writes. |
| `src/solutions/InvoiceReview/src/services/decisionApi.ts` | `BlockedDecisionStamp` mirrors the member (`completedBySystemUserId?: string \| null`). Type only; see "History tab" below. |
| `tests/integration/data-mutation/Ebilling/SubmissionOverrideOperatorFinishTests.cs` | 27 tests, named `iss034c ...` (see "Tests"). |
| `notes/iss034a-operator-finish-stranded-decision.md` | K5 marked fixed by iss034c (EB-D-253). |

No new Decision Record, no Dataverse column, no schema, package, DI or project-file change. The route, its order of checks and the operator record are unchanged.

### Who the completer is

The completer is the user whose request performs the completing stamp write. In `DecideLockedAsync` that is `operatorId`, the caller's own `systemuserid` captured before an operator finish replaces `decider` with the stored decider:

- first-time Override or Return: the decider (the caller);
- the decider's own repeat of a stranded decision: the decider;
- an operator finish: the operator. `bySystemUserId` stays the stored decider, and so do the Signal closes, the stored record and the holds derivation.

The completer is written in the same write that writes the stamp: the release transaction (Override) or the Return path's submission write, so it can never name a caller whose call did not complete the decision. A finish that stops earlier (any refusal, the release or transition faulting, the Signal closes) writes no stamp at all, so no completer; the call that later completes it writes its own caller (tested for another operator and for the decider).

### Where the stamp is built

| Site | What it does | Completer |
|---|---|---|
| Pre-write size check (`findings_too_large`, before the set's store, the Decision Record, the operator record and the closes) | Measures `WithStamp(...).Length` against the column | `operatorId`, so the check measures the stamp as it will be written, including the member (the Guid is fixed-width, so the length is exact) |
| The stamp written with the release / the Return write (and its backstop size check) | The real stamp | `operatorId` |
| `ReasonsAfterReleaseAsync`'s provisional stamp | In memory only, read by the holds derivation (record id and decider only); never written | none ("not recorded"): it keeps the derivation's input unchanged |

### Compatibility

A stamp written before this change has no member and reads as before. A repeat of a finished decision answers `alreadyDecided` and does not rewrite the stamp, so history is not rewritten for a submission whose stamp predates this change (nothing writes the stamp except the completing write).

## History tab: not done (reported, not invented)

The task said to stop after the server part if `HistoryTab` does not show blocked-decision stamps and adding the line would need a new data fetch. That is the case:

- `HistoryTab` renders `review.decisionHistory` (Decision Records, with `decidedBy.name` resolved by the server) and `listCorrections`. It has no access to the blocked submission: `review.submission` is typed `unknown`, and the stamps come only from `GET .../blocked` (`getBlocked`, used by `BlockedBanner`).
- The stamp carries system user ids, not names. The tab's "person-name resolution" is the server's `ReviewRef.name` on each Decision Record; nothing in the page resolves a `systemuserid` to a name. Showing "Finished by {name}" needs a new fetch of the blocked view in the tab plus a new way to resolve the completer's name (for example the server resolving it into the stamp or the review read).

The server part is complete and the GET view (`BlockedSubmissionView.Override` / `.Return`) carries `completedBySystemUserId`. The page line is left for a follow-up that decides where the name comes from.

## Tests

All in `SubmissionOverrideOperatorFinishTests` (the shared `World`, `StrandAsync`, `ActAsAdmin`, `Snapshot` of iss034a).

| Acceptance criterion | Test | Cases |
|---|---|---|
| AC1: first-time Override and Return write the decider | `AFirstTimeDecision_WritesTheDeciderAsTheCompleter` | 2 |
| AC1: the decider's repeat after a stranded write writes the decider | `TheDecidersRepeat_WritesTheDeciderAsTheCompleter` (all six strands; also asserts the stranded write left no stamp) | 6 |
| AC1: an operator finish writes the operator; `bySystemUserId`, the record, the reason and the closes stay the decider's | `AnOperatorFinish_WritesTheOperatorAsTheCompleter_AndKeepsTheDecider` | 6 |
| AC2: a finish that failed before the completing write leaves no completer and no trace of the operator in the findings; a later completion by another operator or by the decider writes that person | `AFailedFinish_WritesNoCompleter_AndALaterCompletionNamesItsOwnCaller` | 4 |
| AC2: refusals write no stamp (no admin marker; the decider as the caller) | `ARefusedFinish_WritesNoCompleter` | 2 |
| AC3 must-not-fire: a stamp without the member, or with a malformed or empty one, reads as null with no fault and stays a valid stamp | `AStampWithoutTheMember_ReadsAsNotRecorded` | 1 |
| AC3: `WithStamp` writes the member only when named, and keeps the other members | `WithStamp_WritesTheMemberOnlyWhenTheStampNamesACompleter` | 1 |
| AC3: the GET view carries the completer; wire name `completedBySystemUserId` | `Get_CarriesTheCompleter` | 2 |
| AC3 must-not-fire: the GET view of an old stamp loads with a null completer | `Get_OfAnOldStamp_HasNoCompleterAndDoesNotFault` | 1 |
| AC4: the pre-check counts the member: padded so the stamp fits only without it, the finish is refused before any write (nothing written); padded to fit exactly with it, it is written | `ThePreCheck_CountsTheCompleter` | 2 |

**Fail-before runs.** (1) The service seeded to the old behaviour (both stamp constructions without the completer): **22 of the 27 failed**; the 5 that passed are the must-not-fire controls and the refusal tests (a refused finish never wrote a completer). (2) Only the pre-check seeded back (the written stamp keeps the completer): the `fitsWithIt: false` case failed (the finish was no longer refused before the writes), the other 26 passed. The service was then restored from a saved copy and the suite re-run green.

## Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror` | 0 warnings, 0 errors |
| BFF tests, filter `SubmissionOverride\|BlockedSubmission` | 538 passed, 0 failed |
| BFF tests, filter `InvoiceOpenHolds\|InvoiceRejectReturn\|InvoiceBlockedHold\|DecisionNote` (other readers of the stamp) | 345 passed, 0 failed |
| ArchTests, filter `Finance
| InvoiceReview `npx jest` | 15 suites, 530 passed, 0 failed |
| InvoiceReview `npm run typecheck` | surface-owned errors 0 (48 pre-existing in shared libs, as on the base) |
| InvoiceReview `npm run build` | succeeded |\|Route` | 195 passed, 0 failed |

## Component justification (CLAUDE.md section 11)

One new member on an existing record and its JSON stamp; no new component. Existing: `BlockedDecisionStamp` / `WithStamp` / `ReadStamp`. Cost of doing nothing: after an operator finish nothing names who completed the decision (K5).
