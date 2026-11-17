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
- After a refusal that says the page is out of date (`operator_not_permitted`, `nothing_stranded`, `own_decision_pending`, `both_decisions_recorded`, `invoice_not_held`, `not_blocked`, `other_decision_pending`) the page re-reads, so the offer follows the server's state. A finished decision is not among them: the finish route answers it 200 `alreadyDecided`, so `already_overridden` and `already_returned` (the other routes' answers) do not re-read.
- Review round 1 (F1): the form no longer goes with the hold. When an unsure outcome (for example a 503 after the decision stood and the invoice was released, with a Signal close failed) makes the re-read show the hold gone, only the banner goes; an open form stays with its error and its lock, so the identical repeat can be sent (the server answers `alreadyDecided` and re-closes). This holds for Override, blocked Return and Finish alike (the form was never tied to `held`, but the whole component returned null; it now returns null only when no submission was ever named, and an open form keeps the submission it was opened for if the re-read names none). The lock is dropped when the form is closed with the hold gone; a form that was never opened is never shown.

## Error code to message

The server's own `detail` is shown when present (ADR-019). The fixed sentence is the fallback when it is missing. All are shown with the code, `sentence (code)`.

| Code | Status | Fixed sentence | Re-read after |
|---|---|---|---|
| `ebilling.blocked.operator_not_permitted` | 403 | You cannot finish another person's decision on this submission. | yes |
| `ebilling.blocked.nothing_stranded` | 409 | No recorded override or return of this submission is waiting to be finished. | yes |
| `ebilling.blocked.both_decisions_recorded` | 409 | Both an override and a return of this submission are recorded, so neither can be finished. Contact support. | yes |
| `ebilling.blocked.own_decision_pending` | 409 | This decision is yours and has not finished. Repeat your own decision to finish it; this route finishes another person's decision. | yes |
| `ebilling.blocked.record_unavailable` | 503 | The record could not be saved right now. Nothing was changed. Try again. | no (not an unknown outcome; the dialog stays editable) |
| `ebilling.blocked.already_overridden` | 409 | This submission was already overridden. | no (the finish route answers 200 instead) |
| `ebilling.blocked.already_returned` | 409 | This submission was already returned to the firm. | no (the finish route answers 200 instead) |
| `ebilling.blocked.invoice_not_held` | 409 | This invoice is no longer held by this submission. | yes |
| `ebilling.blocked.not_authorized` (the close rights the decision needs) | 403 | You do not have the rights to close the signals this decision resolves. | no |
| `ebilling.blocked.decision_conflict` | 409 | A different decision is already recorded for this submission. | no |
| `ebilling.blocked.other_decision_pending` | 409 | The other decision on this submission is recorded but has not finished, so this one cannot be made. | yes |
| `ebilling.blocked.unavailable` (503), `ebilling.blocked.concurrent_change` (409) | | the server's detail, plus "It was saved in part: repeat it to finish." for the 503 | unsure outcome: locks and re-reads |
| `ebilling.note_too_long`, `ebilling.note_invalid_characters` | 400 | under the Note box | no |

Any other code keeps the per-status sentence of `decisionFailureMessage`. `serverWordsPin.test.ts` pins the five new codes, the route, `BlockedDecisionRequest`, the `BlockedStrandedDecision` members and `CanFinishAsOperator` to the server source, and checks that the fixed sentences for the three new 409s are the server's own words.

## Tests

| Where | Count | What |
|---|---|---|
| `finishAsOperator.test.tsx` (new) | 71 | Shows only with `canFinishAsOperator` and a stranded decision (Override and Return); must-not-fire for: no flag, flag left out, no stranded decision, decision left out, the decider (both verbs), an ordinary blocked submission, a failed blocked read, an invoice not held. The dialog names the Override or the Return, needs a reason, posts the trimmed reason (no note key) or the note, refreshes the review, the approval and the blocked view, and keeps a refused note under the Note box. Each of the ten refusal codes: sentence, code, reason kept, not locked; the stale-state refusals re-read, the others do not. Unsure outcomes lock (three kinds), survive reopening, make the other actions warn (both directions), clear on a definite refusal, and stay on a transient one. The client call: route, body, wire casing of the hints, an older server. Dark theme. Review round 1: the server's own detail shown per code; an unsure send whose re-read clears the hold keeps the form open (Override, Return, Finish) and the repeat posts the identical body; must-not-fire for a definite success and for a closed form. |
| `serverWordsPin.test.ts` | +5 | The pins above. |

**Fail-before:** with `BlockedBanner.tsx`, `decisionApi.ts` and `decisionLocks.ts` restored to dfca07840a and the new tests and test kit kept, the two files ran **48 failed, 28 passed** (76 tests; 60 are new). The new tests that pass before are must-not-fire controls and tests of behaviour the old code already had. The files were then restored.

**Review round 1 fail-before:** with `BlockedBanner.tsx` and `decisionApi.ts` restored to 0a6d912e5f and the round-1 tests kept, the two files ran **11 failed, 81 passed** (92 tests). **After:** full InvoiceReview suite **14 suites, 498 tests, 0 failed** (482 after the first build, 422 before the task). `npm run typecheck`: 0 surface-owned errors (99 pre-existing in shared libs, unchanged). `npm run build`: succeeded.

## Known limits

- **Lint not run.** `npm run lint` calls `eslint`, which is not installed in this folder's dependencies (`'eslint' is not recognized`); it fails the same way on the base commit.
- **The offer can be stale.** `canFinishAsOperator` is the server's answer at the last read of the blocked view. If the state changes underneath, the finish route refuses (a code above) and the page re-reads, but the dialog stays open on its error until the reviewer closes it.
- **The dialog's wording names the verb from the hint when it opened.** The dialog opens only from the button, which exists only on the server's hint, so there is always a verb; the generic sentence is for a verb the page does not know.
- **A lost hint strands a locked finish until reload (fails closed).** If a finish is locked (unsure outcome, hold still on) and a re-read loses the hint (the server's hint lookup faulted), the Override and Return forms warn about the unfinished finish and cannot be sent, and there is no way to repeat the finish until the page is reloaded. Deliberately no button outside the server's hint (owner constraint).
- **The fixed sentences are a fallback only.** The server's refusal always carries its own detail, so `BLOCKED_REFUSAL_SENTENCES` is unreachable today; it guards a server that sends none. The tests cover both the detail and the fallback.
- **`record_unavailable` on the finish is treated as definite.** It is not an unknown outcome (the server says nothing was changed), so the form stays editable. A retry with different text keeps the first operator record (the server reuses it) and writes no second decision.
- **No live check.** Tested with mocked BFF clients; not run against a deployed server (no deploy in this task).
