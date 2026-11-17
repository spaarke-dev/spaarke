# ISS-032 (#2883), task iss032: finish an invoice decision stranded after its transition

> **Task:** `tasks/iss032-finish-incomplete-invoice-decision.poml` (FULL) · **Base:** `task/ebilling-iss032` from 8b047c19f1 · **Related:** 095f-2a, 095f-2c, 095f-2ui, iss034b (the UI pattern copied).

## The problem

Reject, return and approve move the invoice's status before they write the Decision Record. When a later step fails, the call answers 503 incomplete and the claimant's repeat finishes it. Once the wizard was closed, or the page reloaded, Invoice Review showed the invoice as decided ("View decision", or nothing at all for an approve), so nothing sent that repeat and the reviewer could not see that the decision was not on record.

## What the repeat needs (the findings the brief asked for)

| Verb | What the repeat takes from the caller | Stored by the server before the step that can fail | Repeat before the record exists | Repeat after the record exists |
|---|---|---|---|---|
| Reject, Return | `reason` (required, checked before anything), `note` (optional) | The claim (decider contact, decision time) on the portion. The invoice's `sprk_decisionnote` holds `note ?? reason` from the transition, so the reason alone cannot be recovered from it. The reason is stored **only** in the Decision Record (`sprk_reason`). | The record takes the repeat's reason and note (095f-2a K-1); the column keeps the first text. | The stored record wins; the request's reason and note are ignored (F1-a). |
| Approve | `note` (optional); no reason, no amount | The claim (decider, time, frozen approved amount) on the portion; the note in the column. | Completion re-runs the approval checks (eligibility included) and records with the portion's amount; the record takes the repeat's note. | The stored record wins; the stored amount must equal the portion's (095f-2a round 2 F2). |

So the repeat can be rebuilt safely in every case: approve needs nothing re-asked, and a reject or return needs its reason re-asked, which the POML provides for ("the page asks for it again, saying why"). This is the existing K-1 "before the record" behaviour, not a new rule, and it does not touch 095f-2c's stored-decision rule (that rule governs a repeat once the record exists, which is unchanged). The escalation trigger was not hit.

## What changed

| File | Change |
|---|---|
| `Services/Finance/InvoiceRejectReturnService.cs` | `KindOfInvoiceStatus` (Rejected / Returned to Firm to the verb), `UnfinishedStepAsync` (read only: the step a reject or return stopped before, or null when it is complete: recorded AND linked) and the one shared record lookup `FindStoredRecordAsync`, which the repeat's `ReplaySignalsAsync` now calls too (one lookup, no second implementation). |
| `Services/Finance/InvoiceApprovalService.cs` | `InvoiceApprovalStateDto.IncompleteDecision` (optional, last positional member, so existing constructions are unchanged) and `InvoiceIncompleteDecisionDto(Verb, FailedStep)`. `GetAsync` reads the caller's contact also for a claimant of a decided invoice, asks the decide right also for that claimant (canApprove still needs eligibility), and adds the hint through `IncompleteDecisionAsync`. |
| `InvoiceReview/src/services/decisionApi.ts` | `ApprovalState.incompleteDecision?` and `IncompleteDecision`. |
| `InvoiceReview/src/services/decisionModel.ts` | `DecisionAccess` gains `finish`; `incompleteOutcome`, `incompleteDecisionText`, `finishDecisionIntro`; `awaitingLine` says "…, not finished" while the hint shows. |
| `InvoiceReview/src/components/decision/DecideInvoiceAction.tsx` | New `IncompleteDecisionBanner` (MessageBar + `FormModal`, copied from BlockedBanner's Finish as operator). The page-level wizard lock is no longer cleared while the hint shows. No footer button while the banner owns the decision. |
| `InvoiceReview/src/components/InvoiceReviewWithDecisions.tsx` | One line: the banner in the `banners` slot (see Deviations). |

### The order of checks (server, `GetAsync`)

1. The invoice has exactly one portion and its status is a decision's target: Approved (approve), Rejected (reject), Returned to Firm (return). Otherwise no hint and no extra read.
2. The portion names a decider (the claim). The caller's own contact (EB-D-171, the recorder's rule) must be that decider: the **claimant**. A contact read fault is no hint. Another eligible approver, a caller with no claim, or an unproven identity gets none: their repeat is a no-op (reject, return) or refused `claimed` (approve).
3. The decide right (Write on the invoice, `ebilling.invoice.decide`, asked as the caller, the decide routes' filter). No right, no caller token, or a fault: no hint.
4. Is it complete? Complete means the repeat's own definition (`CompleteOrNoOpAsync`): the portion has the decision's status AND names its record. A portion that records another decision is not this one's (none). The record lookup is exactly the repeat's:
   - reject / return (`UnfinishedStepAsync`):
     - a linked portion in the verb's status is complete (none);
     - a linked portion not in the verb's status is `link`;
     - otherwise the writer's stored-set lookup under the derived id (`RejectDecisionIdFor` / `ReturnDecisionIdFor`) through `FindStoredRecordAsync`, which the repeat's `ReplaySignalsAsync` also calls. Found is `link`.
   - approve (`UnfinishedApproveStepAsync`):
     - an Approved portion uses `approveRecorded`, the writer-created record match completion uses, already read for the state, so it is not read again;
     - a Pending portion reads `WriterApproveRecordsAsync` under `DecisionIdFor`;
     - with the record, an Approved and linked portion is complete (none), anything else is `link`.
   - No record: `record` when the portion's status already moved, `portion` when it did not.
5. Eligibility (approve only, and only without a record). Approve's repeat re-checks eligibility (`CheckCompletionAsync`) only while no writer-created record exists, so `record` and `portion` are hinted only to an eligible approver. `link` is hinted without it. Reject and return's repeat never re-checks eligibility, so neither does their hint.
6. A fault in step 4 gives no hint (never a guess) and logs one Warning with the invoice id, the verb and the fault type or refusal reason. No exception text.

### The page

- `incompleteDecision` with a known verb makes `decisionAccess` `finish`: the banner "This decision did not finish" says what happened and offers **Finish decision…**; "View decision" and "Decide invoice…" are not shown (the read-only view would show a decision that is not on record). A verb the page does not know offers nothing.
- The Finish form sends the same repeat the open wizard would: `approve(id, note?)`, `reject(id, reason, note?)`, `returnToFirm(id, reason, note?)`. A reject or return asks for the reason again and says why (the reason is kept only in the record that was not written). When this page still holds the request it sent (the wizard's or this form's unsure send, page-level `locks`), the form opens with that request, locked, and says it repeats it.
- **After the record (`failedStep` `link`).**
  - The banner and the header line say the decision is recorded and only its follow-up steps did not finish.
  - The form asks nothing. It does not say the reason was not saved, and it shows no Note box, because the server drops a note once the record exists.
  - A reject or return still has to send a non-empty reason, which the server ignores in favour of the stored one. The form sends the reason the decision's invoice-level Decision Record holds (`recordedReason`), shown read-only.
    - Source of that reason: the review's `decisionHistory`. It is already loaded, and the server reads it by the invoice the records regard, so a record not yet linked is in it.
    - When the review does not carry it (no matching record, or no reason), Finish is disabled and the form says to reload. The user is never asked to type it, and nothing is guessed.
  - Approve sends the approve alone.
- One repeat at a time: `FormModal` `busy` disables Finish while the call is out, and a ref drops a second click before that renders.
- Success: the lock is cleared, the form closes, the approval and the review are re-read; the hint goes and "View decision" comes back.
- A refusal or a 503 is shown in the form with the server's detail and code (`decisionFailureMessage`), the reason is kept, and the banner keeps Finish while the server still reports the decision. An unsure outcome (503 incomplete, no answer) locks the reason and note, here and at page level, and re-reads; a 409 re-reads too (the wizard's rule). A refused note goes under the Note box. An open form is not tied to the banner: when a re-read shows the hint gone, it stays open with its error so the same request can be sent again (iss034b round 1).

## Tests

| Where | Count | What |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceDecisionIncompleteHintTests.cs` (new) | 34 (23 + 11 in review round 1) | Must fire: reject, return and approve stranded at `portion`, at the record (`identity`, the recorder's contact read) and after an unknown-outcome transition report the verb and the step; the claimant's repeat then finishes it (one record) and the hint goes. Stranded after the record (a Signal not closed, or the link) reports `link` for each verb; the repeat closes the Signal and links from the stored record (no second record) and the hint goes. A recorded decision whose portion is back at Pending reports `link`. A reject or return claimant no longer eligible is still hinted, and so is an approve claimant after the record; their repeat finishes it. Must not fire: a complete decision (record written AND linked) reports none; an Approved invoice whose portion records another decision (K1) reports none; an undecided invoice reports none. A lookup fault (the writer's lookup for reject and return, the approve record read for approve) reports none, logs one Warning with ids only, and the next read reports it. A caller who may not make it gets none: another eligible approver, a caller with no claim, no identity, the claimant without the decide right or with no caller to ask, an approve claimant no longer eligible, and a claimant whose contact read faults. |
| `src/solutions/InvoiceReview/src/__tests__/finishIncompleteDecision.test.tsx` (new) | 30 (25 + 5 in review round 1) | After the record (`link`): the lenses, the banner and header wording, reject and return sending the recorded reason read-only with no note exactly once, approve sending the approve alone, and a review with no record reason disabling Finish. The pure lenses (access, outcome, header line, wording; must-not-fire for no hint, a hint left out, an unknown verb). After a reload, each verb shows the stranded message and Finish decision and no View decision; a complete decision shows only View decision (each verb); an older server and an unknown verb show View decision; a pending invoice shows Decide. Finish: reject and return ask the reason (blank does not count) and send exactly one repeat with the trimmed reason and no note key, then show the decision read-only; approve asks no reason and sends no note key; the note is sent as typed; the button is disabled while in flight and a second click sends nothing. A 403, a 409 and a 503 that changed nothing show their message and code, keep the reason editable and keep the Finish affordance; a 503 saved in part locks, keeps Finish, and the repeat is the identical request; the lock survives closing and reopening; a refused note goes under the Note box; an unsure send whose re-read clears the hint keeps the open form. Dialogs are found by role and checked by their text, never by accessible name (ISS-036). |
| `src/solutions/InvoiceReview/src/__tests__/serverWordsPin.test.ts` | +2 | `InvoiceIncompleteDecisionDto(Verb, FailedStep)` and the state member are the server's; the verbs are the `StrandedKind` names the server sends. |

**Fail-before (server).** With the hint forced to null in `GetAsync` (the DTO kept so the tests compile), the new suite ran **17 failed, 6 passed** of 23; the 6 are the must-not-fire cases. Restored.

**Fail-before (page).** I swapped `DecideInvoiceAction.tsx` and `InvoiceReviewWithDecisions.tsx` back to 8b047c19f1 and kept the new model so the suite compiles. The new suite then ran **15 failed, 10 passed** of 25. All 15 failures are behaviour tests: there was no banner and no Finish button. The 10 passes are the pure lens tests and the must-not-fire cases. Then I restored my versions.

**After.**
- Server:
  - `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror`: 0 warnings, 0 errors.
  - Unit tests (filter `InvoiceRejectReturn|InvoiceApproval|InvoiceDecision|InvoiceReview`): **611/611**.
  - ArchTests (filter `Finance|Route`): **195/195**.
- Page:
  - `npx jest`: **16 suites, 557/557**. Of these, `finishIncompleteDecision` is 25/25 and `serverWordsPin` is 23/23.
  - Typecheck (`tsc-surface-gate`): 0 surface-owned errors. The 99 pre-existing errors are in the shared libraries.
  - `npm run build`: passes.

## Review round 1

The coordinator amended the must-not-fire. It was "a decision whose record exists"; it is now "a complete decision: record written AND linked", the server's own `CompleteOrNoOpAsync` definition. The POML constraint and the xUnit criterion now say so.

**Fixed**
- **F4: a decision stranded after its record had no hint.**
  - Now it is reported with `failedStep` `link`, with no new reads (see the order of checks).
  - Approve's `link` skips the eligibility gate. I checked the code before relying on that: `CompleteOrNoOpAsync` (approve) reaches the claimant's repeat before any eligibility check, and `CheckCompletionAsync` runs only when `recorded.Count == 0`.
  - The page says the decision is recorded and only its follow-up steps are outstanding. Finish sends a reject or return with the record's reason, read-only and taken from the review, and asks nothing.
  - The tests that pinned the defect are now must-fire tests for `signals` and `link`, per verb, each with a repeat that clears the hint.
- **F2:** `DecideInvoiceAction.tsx` imports `REASON_MAX` from `./steps` instead of declaring it again.
- **K1:** the approve hint returns none when the portion is neither Pending/null nor Approved, as reject and return do. One test.
- **K3 (optional, done):** an Approved portion's record is not read a second time; `approveRecorded` is that same match. A Pending portion's record is still read, since `approveRecorded` is not computed for it.

**Known limits**
- **K2: the 105-return false positive is unreachable.** The hint needs the caller's own claim on the portion (`sprk_decidedby`), and a 105 return writes none.
- **K3:** see Fixed.
- **K4 (carried limits, confirmed by the review):** only the claimant sees the hint, so if they have left nobody can finish it from the page; the Signal close rights are not pre-checked, so a caller without them gets a 403 in the form; a re-asked reason (before the record) can differ from the first text (095f-2a K-1).
- **Scoped re-check (Sonnet, after round 1):** no F-class finding. K: a non-empty link counts as complete without checking that it names the writer's record, for reject and return as well as approve (a hand-edited link shows "View decision" while the repeat would still repair it). K: `recordedReason` has no test with two records of different verbs; the server ignores the reason at the link stage, so a wrong choice is harmless.

**Fail-before**
- Server: the round-0 services from 0a11e3f357 against the new suite gave **11 failed, 23 passed** of 34. The 11 are the new tests.
- Page: the round-0 components from f56d1d875f, with the new model so the suite compiles, gave **4 failed, 26 passed** of 30. The 4 are the link-stage component tests; the lens test passes because the model is new.
- Both were restored afterwards.

**After**
- Server:
  - build: 0 warnings, 0 errors;
  - unit tests (filtered as above): **622/622**;
  - ArchTests (filtered as above): **195/195**.
- Page:
  - `npx jest`: **16 suites, 562/562**;
  - typecheck: 0 errors in the page's own files;
  - `npm run build`: passes.

## Known limits

- **The approve link check trusts any link (review round 1).** For an Approved portion, `approveRecorded` says a writer-created approve record exists but not which one. A portion that is linked counts as complete without checking that the link names that record. Completion checks this (the link must be one of the writer's records), but the hint would need a second read to do the same. The portion's link is written only by the services, so a mismatch means a hand-edited column. That case shows "View decision", and the claimant's approve would still finish it.
- **A reject or return reason the review does not carry blocks Finish after the record.** This happens when the review's history read fails or the record holds no reason. The form says to reload; there is no other way to send the stored reason without a new read.
- **Only the claimant sees it.** A stranded decision whose decider has left, or who lacks the decide right, is shown to nobody (another approver's repeat is a no-op for reject and return, and refused `claimed` for approve). That is the server's existing repeat rule (the POML: the hint never widens it). An operator path, as ISS-034 built for blocked decisions, would be a separate task.
- **The close rights are not pre-checked.** The repeat asks the caller's rights to close the invoice's Signals; the hint does not. A claimant without them sees Finish and gets the 403 in the form (shown, affordance kept).
- **The re-asked reason may differ from the first.** Before the record exists, the record takes the reason and note the reviewer types now; the invoice's decision note column keeps what the first call's transition wrote. That is 095f-2a K-1 as accepted; the dialog tells the reviewer the reason was not saved. The first text is not shown back (the column holds the note or, with none, the reason, and the two cannot be told apart).
- **Approve's completion checks still apply.** A repeat that is refused 409 `completion_refused` is shown in the form (for example, when a line changed since). The hint stays, since the record is still missing. An administrator path is out of scope.
- **Lint not run.** `npm run lint` needs `eslint`, which is not installed in this folder (as noted in iss034b).
- **No live check.** Mocked BFF clients and the in-memory Dataverse only; nothing deployed.

## Deviations

- **`InvoiceReviewWithDecisions.tsx` changed by one line** (the import and the banner in the `banners` slot). The POML lists DecideInvoiceAction.tsx, decisionModel.ts and decisionApi.ts "as needed". The banner pattern it asks for (iss034b's MessageBar) lives in the page's `banners` slot, which only this composition file fills; the `invoiceActions` slot is the footer row, which cannot hold a MessageBar. The banner component itself lives in `DecideInvoiceAction.tsx`.
- **`InvoiceApprovalEndpoints.cs` is unchanged.** `InvoiceApprovalStateDto` is declared in `InvoiceApprovalService.cs`; the endpoint returns it as before.
- **The `testKit.ts` and `serverWordsPin.test.ts` test files** gained builders and pins (tests of the files above).

## Component justification (CLAUDE.md §11)

New surface: one optional DTO member and its record, one internal static read on the reject/return service, one private method on the approval service, one React component.

1. **Existing:** the approval-state read (`GetAsync`), the claimant's repeat (`CompleteOrNoOpAsync` on both services) and its record lookups, BlockedBanner's Finish as operator (the UI).
2. **Extension:** all extended in place: the state read gains a member, the reject/return record lookup is factored once and called by both the repeat and the hint, the UI copies iss034b's banner and form. No new endpoint, write path, service or registration.
3. **Cost of doing nothing:** a stranded reject, return or approve is never finished once the wizard closes: no Decision Record, no note in the record, no Signal closes and no link, and the reviewer cannot see it.
