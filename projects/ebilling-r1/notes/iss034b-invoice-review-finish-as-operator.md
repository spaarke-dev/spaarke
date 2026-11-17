# ISS-034 (#2888), task iss034b: Invoice Review offers "Finish as operator" on a stranded blocked decision

> **Task:** `tasks/iss034b-invoice-review-finish-as-operator.poml` (STANDARD) · **Decision:** EB-D-244 · **Server contract:** `iss034a-operator-finish-stranded-decision.md` · **Base:** `task/ebilling-iss034b` from dfca07840a · **Scope:** `src/solutions/InvoiceReview/**` only (no shared library, no server, no deploy).

## What changed

| File | Change |
|---|---|
| `src/services/decisionApi.ts` | `BlockedSubmission` gains optional `strandedDecision` (`{ verb, recordId, callerIsDecider }`) and `canFinishAsOperator` (optional, so an older server reads as none / false). New `finishStranded(submissionId, reason, note?)` on `InvoiceDecisionApi` and the route `POST /api/finance/submissions/{id}/finish-stranded`, body `{ reason, note? }` (the note key is left out when blank, as every decision does), reply read as `BlockedDecisionResponse`. New code constants for the five codes ISS-034a added, `BLOCKED_REFUSAL_SENTENCES` (a fixed sentence per code, used only when the server sends no `detail`) and `isFinishStaleRefusal`. |
| `src/components/decision/BlockedBanner.tsx` | A third action, `Finish as operator…` (`data-testid="finish-stranded"`), shown only when `canFinishAsOperator === true`, a `strandedDecision` is present and its `callerIsDecider` is false. Its dialog names the pending decision (an override or a return recorded by another person), says it is finished exactly as recorded, and takes a required reason and the optional `NoteField`. Submit label `Finish`. It reuses the Override and Return flow: `locks` (page-level lock on an unsure outcome), `isBlockedOutcomeUnknown`, `isBlockedDefiniteRefusal`, `isNoteRefusal`, refresh of the review, the approval and the blocked view after success. The "other action is unfinished" warning is now built from the two actions involved (the existing Override and Return sentences are unchanged word for word). |
| `src/components/decision/decisionLocks.ts` | `BlockedLock.action` also accepts `'finish'`. |
| `src/__tests__/testKit.ts` | `blockedSubmission` gets the two hint fields (null / false); new `strandedSubmission`, `STRANDED_RECORD_ID`; `finishStranded` on the mock decision API. |
| `src/__tests__/finishAsOperator.test.tsx` (new), `src/__tests__/serverWordsPin.test.ts` | The tests below. |

## How it behaves

- The page never decides authority. The action is offered only on the server's `canFinishAsOperator`; a stored decider (`callerIsDecider`) never sees it (they repeat their own Override or Return).
- A finish posts the trimmed reason (and the note as typed when there is one), then re-reads the review, the approval and the blocked view; the banner goes with the hold.
- An unsure outcome (503 `ebilling.blocked.unavailable`, 409 `concurrent_change`, no answer) locks the dialog as it was sent and keeps the lock at page level; reopening shows it locked, the repeat is the same request, and a definite refusal on the repeat clears it. While a finish is locked, Override and Return open with the "may have been saved in part" warning and cannot be sent, and the reverse holds for the finish.
- Every refusal stays in the dialog with the server's `detail` (or the fixed sentence below) and the code in brackets; the reason and note are kept. A 400 note refusal shows under the Note box.
- After a refusal that says the page is out of date (`operator_not_permitted`, `nothing_stranded`, `own_decision_pending`, `both_decisions_recorded`, `already_overridden`, `already_returned`, `not_blocked`, `other_decision_pending`) the page re-reads, so the offer follows the server's state.

## Error code to message

The server's own `detail` is shown when present (ADR-019). The fixed sentence is the fallback when it is missing. All are shown with the code, `sentence (code)`.

| Code | Status | Fixed sentence | Re-read after |
|---|---|---|---|
| `ebilling.blocked.operator_not_permitted` | 403 | You cannot finish another person's decision on this submission. | yes |
| `ebilling.blocked.nothing_stranded` | 409 | No recorded override or return of this submission is waiting to be finished. | yes |
| `ebilling.blocked.both_decisions_recorded` | 409 | Both an override and a return of this submission are recorded, so neither can be finished. Contact support. | yes |
| `ebilling.blocked.own_decision_pending` | 409 | This decision is yours and has not finished. Repeat your own decision to finish it; this route finishes another person's decision. | yes |
| `ebilling.blocked.record_unavailable` | 503 | The record could not be saved right now. Nothing was changed. Try again. | no (not an unknown outcome; the dialog stays editable) |
| `ebilling.blocked.already_overridden` | 409 | This submission was already overridden. | yes |
| `ebilling.blocked.already_returned` | 409 | This submission was already returned to the firm. | yes |
| `ebilling.blocked.not_authorized` (the close rights the decision needs) | 403 | You do not have the rights to close the signals this decision resolves. | no |
| `ebilling.blocked.decision_conflict` | 409 | A different decision is already recorded for this submission. | no |
| `ebilling.blocked.other_decision_pending` | 409 | The other decision on this submission is recorded but has not finished, so this one cannot be made. | yes |
| `ebilling.blocked.unavailable` (503), `ebilling.blocked.concurrent_change` (409) | | the server's detail, plus "It was saved in part: repeat it to finish." for the 503 | unsure outcome: locks and re-reads |
| `ebilling.note_too_long`, `ebilling.note_invalid_characters` | 400 | under the Note box | no |

Any other code keeps the per-status sentence of `decisionFailureMessage`. `serverWordsPin.test.ts` pins the five new codes, the route, `BlockedDecisionRequest`, the `BlockedStrandedDecision` members and `CanFinishAsOperator` to the server source, and checks that the fixed sentences for the three new 409s are the server's own words.

## Tests

| Where | Count | What |
|---|---|---|
| `finishAsOperator.test.tsx` (new) | 55 | Shows only with `canFinishAsOperator` and a stranded decision (Override and Return); must-not-fire for: no flag, flag left out, no stranded decision, decision left out, the decider (both verbs), an ordinary blocked submission, a failed blocked read, an invoice not held. The dialog names the Override or the Return, needs a reason, posts the trimmed reason (no note key) or the note, refreshes the review, the approval and the blocked view, and keeps a refused note under the Note box. Each of the ten refusal codes: sentence, code, reason kept, not locked; the stale-state refusals re-read, the others do not. Unsure outcomes lock (three kinds), survive reopening, make the other actions warn (both directions), clear on a definite refusal, and stay on a transient one. The client call: route, body, wire casing of the hints, an older server. Dark theme. |
| `serverWordsPin.test.ts` | +5 | The pins above. |

**Fail-before:** with `BlockedBanner.tsx`, `decisionApi.ts` and `decisionLocks.ts` restored to dfca07840a and the new tests and test kit kept, the two files ran **48 failed, 28 passed** (76 tests; 60 are new). The new tests that pass before are must-not-fire controls and tests of behaviour the old code already had. The files were then restored.

**After:** full InvoiceReview suite **14 suites, 482 tests, 0 failed** (422 before this task). `npm run typecheck`: 0 surface-owned errors (99 pre-existing in shared libs, unchanged). `npm run build`: succeeded.

## Known limits

- **Lint not run.** `npm run lint` calls `eslint`, which is not installed in this folder's dependencies (`'eslint' is not recognized`); it fails the same way on the base commit.
- **The offer can be stale.** `canFinishAsOperator` is the server's answer at the last read of the blocked view. If the state changes underneath, the finish route refuses (a code above) and the page re-reads, but the dialog stays open on its error until the reviewer closes it.
- **The dialog's wording names the verb from the hint at the time it opened.** A locked reopening after the hint is gone uses a generic sentence.
- **No live check.** Tested with mocked BFF clients; not run against a deployed server (no deploy in this task).
