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
| `Services/Finance/InvoiceRejectReturnService.cs` | `KindOfInvoiceStatus` (Rejected / Returned to Firm to the verb), `UnrecordedStepAsync` (read only: the step a reject or return stopped before, or null when it has its record) and the one shared record lookup `FindStoredRecordAsync`, which the repeat's `ReplaySignalsAsync` now calls too (one lookup, no second implementation). |
| `Services/Finance/InvoiceApprovalService.cs` | `InvoiceApprovalStateDto.IncompleteDecision` (optional, last positional member, so existing constructions are unchanged) and `InvoiceIncompleteDecisionDto(Verb, FailedStep)`. `GetAsync` reads the caller's contact also for a claimant of a decided invoice, asks the decide right also for that claimant (canApprove still needs eligibility), and adds the hint through `IncompleteDecisionAsync`. |
| `InvoiceReview/src/services/decisionApi.ts` | `ApprovalState.incompleteDecision?` and `IncompleteDecision`. |
| `InvoiceReview/src/services/decisionModel.ts` | `DecisionAccess` gains `finish`; `incompleteOutcome`, `incompleteDecisionText`, `finishDecisionIntro`; `awaitingLine` says "…, not finished" while the hint shows. |
| `InvoiceReview/src/components/decision/DecideInvoiceAction.tsx` | New `IncompleteDecisionBanner` (MessageBar + `FormModal`, copied from BlockedBanner's Finish as operator). The page-level wizard lock is no longer cleared while the hint shows. No footer button while the banner owns the decision. |
| `InvoiceReview/src/components/InvoiceReviewWithDecisions.tsx` | One line: the banner in the `banners` slot (see Deviations). |

### The order of checks (server, `GetAsync`)

1. The invoice has exactly one portion and its status is a decision's target: Approved (approve), Rejected (reject), Returned to Firm (return). Otherwise no hint and no extra read.
2. The portion names a decider (the claim). The caller's own contact (EB-D-171, the recorder's rule) must be that decider: the **claimant**. A contact read fault is no hint. Another eligible approver, a caller with no claim, or an unproven identity gets none: their repeat is a no-op (reject, return) or refused `claimed` (approve).
3. The decide right (Write on the invoice, `ebilling.invoice.decide`, asked as the caller, the decide routes' filter). No right, no caller token, or a fault: no hint.
4. Approve only: the caller is an eligible approver now (`CheckCompletionAsync` re-checks it before the repeat writes the record). Reject and return's repeat does not re-check eligibility, so neither does their hint.
5. The record lookup, exactly as the repeat makes it:
   - reject / return (`UnrecordedStepAsync`): a portion that records the other decision is not this one (none); a portion that names a record has it (none); else the writer's stored-set lookup under the derived id (`RejectDecisionIdFor` / `ReturnDecisionIdFor`) through `FindStoredRecordAsync`; found is none.
   - approve: `WriterApproveRecordsAsync` under `DecisionIdFor` (the writer-created record match completion uses). Skipped when `approveRecorded` (the same match, already read for the state) is true.
   - Missing: `{ verb, failedStep }`, where `failedStep` is `record` when the portion's status already moved and `portion` when it did not.
6. A fault in step 5 gives no hint (never a guess) and logs one Warning with the invoice id, the verb and the fault type or refusal reason. No exception text.

### The page

- `incompleteDecision` with a known verb makes `decisionAccess` `finish`: the banner "This decision did not finish" says what happened and offers **Finish decision…**; "View decision" and "Decide invoice…" are not shown (the read-only view would show a decision that is not on record). A verb the page does not know offers nothing.
- The Finish form sends the same repeat the open wizard would: `approve(id, note?)`, `reject(id, reason, note?)`, `returnToFirm(id, reason, note?)`. A reject or return asks for the reason again and says why (the reason is kept only in the record that was not written). When this page still holds the request it sent (the wizard's or this form's unsure send, page-level `locks`), the form opens with that request, locked, and says it repeats it.
- One repeat at a time: `FormModal` `busy` disables Finish while the call is out, and a ref drops a second click before that renders.
- Success: the lock is cleared, the form closes, the approval and the review are re-read; the hint goes and "View decision" comes back.
- A refusal or a 503 is shown in the form with the server's detail and code (`decisionFailureMessage`), the reason is kept, and the banner keeps Finish while the server still reports the decision. An unsure outcome (503 incomplete, no answer) locks the reason and note, here and at page level, and re-reads; a 409 re-reads too (the wizard's rule). A refused note goes under the Note box. An open form is not tied to the banner: when a re-read shows the hint gone, it stays open with its error so the same request can be sent again (iss034b round 1).

## Tests

| Where | Count | What |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceDecisionIncompleteHintTests.cs` (new) | 23 | Must fire: reject, return and approve stranded at `portion`, at the record (`identity`, the recorder's contact read) and after an unknown-outcome transition report the verb and the step; the claimant's repeat then finishes it (one record) and the hint goes. A reject or return claimant no longer eligible is still hinted, and their repeat finishes it. Must not fire: a complete decision and one stranded after its record (`link`) report none; an undecided invoice reports none. A lookup fault (the writer's lookup for reject and return, the approve record read for approve) reports none, logs one Warning with ids only, and the next read reports it. A caller who may not make it gets none: another eligible approver, a caller with no claim, no identity, the claimant without the decide right or with no caller to ask, an approve claimant no longer eligible, and a claimant whose contact read faults. |
| `src/solutions/InvoiceReview/src/__tests__/finishIncompleteDecision.test.tsx` (new) | 25 | The pure lenses (access, outcome, header line, wording; must-not-fire for no hint, a hint left out, an unknown verb). After a reload, each verb shows the stranded message and Finish decision and no View decision; a complete decision shows only View decision (each verb); an older server and an unknown verb show View decision; a pending invoice shows Decide. Finish: reject and return ask the reason (blank does not count) and send exactly one repeat with the trimmed reason and no note key, then show the decision read-only; approve asks no reason and sends no note key; the note is sent as typed; the button is disabled while in flight and a second click sends nothing. A 403, a 409 and a 503 that changed nothing show their message and code, keep the reason editable and keep the Finish affordance; a 503 saved in part locks, keeps Finish, and the repeat is the identical request; the lock survives closing and reopening; a refused note goes under the Note box; an unsure send whose re-read clears the hint keeps the open form. Dialogs are found by role and checked by their text, never by accessible name (ISS-036). |
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

## Known limits

- **A decision stranded after its record is not hinted.** The POML defines the hint by the missing record, and "a decided invoice whose Decision Record exists is unchanged" is a must-not-fire. A reject, return or approve that stopped at `signals` or `link` (the record written, some Signals not closed, or the portion not linked) therefore still shows "View decision", and nothing on the page sends the repeat that would close the remaining Signals and write the link. The same happens when a Finish repeat itself stops after the record: the open form keeps its error and can be resent, but once it is closed the page has nothing to offer. The record (and the decision's reason) is there in this case, so the user-visible loss is smaller: open Signals and an unlinked portion. A follow-up would hint that case too (failedStep `signals` / `link`) and let Finish send any non-empty reason, since the server ignores it once the record exists. Not filed by this task; for the coordinator.
- **Only the claimant sees it.** A stranded decision whose decider has left, or who lacks the decide right, is shown to nobody (another approver's repeat is a no-op for reject and return, and refused `claimed` for approve). That is the server's existing repeat rule (the POML: the hint never widens it). An operator path, as ISS-034 built for blocked decisions, would be a separate task.
- **The close rights are not pre-checked.** The repeat asks the caller's rights to close the invoice's Signals; the hint does not. A claimant without them sees Finish and gets the 403 in the form (shown, affordance kept).
- **The re-asked reason may differ from the first.** Before the record exists, the record takes the reason and note the reviewer types now; the invoice's decision note column keeps what the first call's transition wrote. That is 095f-2a K-1 as accepted; the dialog tells the reviewer the reason was not saved. The first text is not shown back (the column holds the note or, with none, the reason, and the two cannot be told apart).
- **Approve's completion checks still apply.** A repeat refused 409 `completion_refused` (for example a line changed since) is shown in the form; the hint stays, since the record is still missing. An administrator path is out of scope.
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
