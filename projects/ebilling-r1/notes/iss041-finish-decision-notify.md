# ISS-041 (#2930): finishing a stranded reject or return after a page reload can send the firm notice

Branch `task/ebilling-iss041`, from `b4746e24df` (`work/ebilling-r1`). Rigor FULL. Model: Sonnet @ high. Option (a) of ISS-041.

Client-only (`src/solutions/InvoiceReview`). No server, route, schema, shared-library or `.claude/` change. Not merged, no PR.

## The server contract (read, not changed)

`InvoiceRejectReturnEndpoints.cs` `DecideAsync`: the notice (`NoticeSubject`, `NoticeProse`) is read from the request body of every reject or return, including a repeat. It is screened first (`FirmNoticeProse.ScreenConfirmed`, 400 `ebilling.notice_refused` before anything is decided), and when the service answers `Decided` the notice rides on the enqueued job. A repeat that finishes a stranded decision (`CompleteOrNoOpAsync` to `FinishAsync`) answers `Decided`, in the "record" and the "link" stage alike, so the repeat's body is the only place the notice can come from. The server needs no change.

## What changed

| File | Change |
|---|---|
| `src/components/decision/DecideInvoiceAction.tsx` (`IncompleteDecisionBanner`) | When the form opens for a reject or return and the page's lock holds no notice (`locks.get('wizard')?.notice` absent: a reload, or the first attempt came from another tab), `dialog.offerNotice` is set and the form renders the wizard's own `NotifyStep` (inside a `WizardContext.Provider` built from the form's state; nothing in `NotifyStep` was changed). The form holds the entries (`notices`, one per kind), asks for the draft when it opens, holds Finish while the draft loads or the notice is blank or over a limit with sending on (`noticeBlocker`), and sends `reject` / `returnToFirm` with `(id, reason, note, notice)` when `noticeToSend` gives one, else the old call exactly. The switch "Send this notice to the firm" off sends none. A 400 `notice_refused` shows under the notice fields (the step's own bar); an edit clears it. An unsure send keeps what it sent in the page lock (`noticeLockOf`), so the form then shows it read-only like a kept notice and the repeat is the same request. The medium form size is used when the step is offered. |
| `src/components/decision/useNoticeDrafts.ts` (new) | The draft call, the once-per-kind rule and the failure wording, lifted out of the wizard so the wizard and the Finish form share them (no copy). |
| `src/components/decision/InvoiceDecisionWizard.tsx` | `loadNotice` now comes from `useNoticeDrafts` (same behaviour; the late-answer rule is `settleNoticeEntries`). |
| `src/services/noticeModel.ts` | `settleNoticeEntries`: an answer lands only on an entry still `loading`. |
| `src/__tests__/finishIncompleteDecision.test.tsx` | New block "ISS-041" (10 tests); the five existing reject / return finish tests now expect the offered notice (the repeat carries the drafted notice, sending on by default). |
| `src/__tests__/firmNoticeStep.test.tsx` | A must-not-fire test: with the notice kept in memory (on or off) the Finish form offers no Notify step and asks for no second draft. |

## Behaviour

| Case | Result |
|---|---|
| Reload, stranded reject or return (both stages: no record yet, or record exists and follow-up steps failed) | Finish form shows the Notify step (draft for the kind, editable, limits and counters as in the wizard); the repeat carries the confirmed trimmed notice. |
| Reviewer turns "Send this notice" off | The repeat is the old call: no notice fields. |
| Draft could not be fetched | Message, code and Retry; Finish is not blocked; no notice sent. |
| Draft loading, or notice blank or over its limit with sending on | Finish held (as the wizard holds Next). |
| Server refuses the notice (400) | Shown under the fields; nothing decided; edit then Finish again. |
| Unsure answer from this form | The notice (or the off choice) is kept in the page lock; the form then shows it read-only; the repeat is identical. |
| Notice kept in memory (no reload) | Unchanged: read-only line, the repeat carries it (132ui F1-a). |
| Approve finish | Unchanged: no Notify fields, no draft call. |

## Acceptance criteria to tests (`finishIncompleteDecision.test.tsx`, block ISS-041 unless named)

| Criterion | Test |
|---|---|
| After a reload, reject and return offer Notify and the repeat carries the notice | "%s: the form offers the Notify step ..." (Reject, Return); the updated "link" stage tests carry it too |
| The choice to send none | "%s: choosing to send no notice sends none ..." (Reject, Return); "an unsure send with the notice turned off keeps that choice" |
| Same screening as the original Notify step | "a blank or over-limit notice ... holds Finish"; "Finish is held while the draft loads"; "a refused notice is shown under the notice fields" |
| Failure of the draft never blocks | "a draft that could not be fetched ..." |
| Without a reload the finish is as before | `firmNoticeStep.test.tsx` "ISS-041 (must-not-fire) ..." and the existing F1-a tests there |
| Approve shows no Notify fields | "(must-not-fire) an approve finish ..." |

## Results

- `npx jest` in `src/solutions/InvoiceReview` (the page uses jest, not vitest): 18 suites, **645 passed, 0 failed** (632 before: +13 new tests).
- `npm run build`: passes (surface gate 0 errors, vite build 3523 modules).
- Failing before, passing after: with `DecideInvoiceAction.tsx` restored to `b4746e24df` and the new tests in place, `finishIncompleteDecision.test.tsx` + `firmNoticeStep.test.tsx` gave 23 failed, 70 passed; with the change, 93 passed, 0 failed. (The five edited existing tests and the ISS-041 block fail on the old banner; the Approve and kept-notice must-not-fire tests pass on both, as intended.)
- `npm run typecheck` (surface gate): Surface-owned 0 errors; the 99 shared-library errors are the known pre-existing ones.

## Fix round 1 (adversarial review of `d206838a2a`)

| Finding | Change | Test |
|---|---|---|
| F1: an unsure send from the offered form kept the notice in the lock and removed Notify; if the repeat then got 400 `notice_refused`, the form was stuck (reason read-only, Finish resent the refused notice). | `DecideInvoiceAction.tsx` `submit`: on a notice refusal while a notice is kept (`noticeLock`), `locks.clear('wizard')`, `setNoticeLock(undefined)`, `setLocked(false)` unless `recorded` (the record's reason stays read-only), and `offerNotice` is set so the Notify step is offered again. A notice kept from the wizard (never composed in this form) seeds the Notify entry from the lock, so the reviewer edits the text as sent (no second draft). A composed one keeps its `notices` entry. The refusal shows under the fields. Mirrors the wizard's review round 2. | `finishIncompleteDecision.test.tsx` "F1": unsure send, refusal, Notify back, edit, Finish sends the edited notice; turn it off after the refusal sends none; (must-not-fire) an unsure send not refused stays locked. `firmNoticeStep.test.tsx`: the wizard-kept notice refused on the banner repeat becomes editable. |
| F2: the recorded-stage intro said "nothing can be changed" while a Notify step was offered; with an unreadable recorded reason (Finish off) Notify was still offered and drafted. | `finishDecisionIntro(outcome, repeatsSent, recorded, noticeOffered)` says "The notice to the firm can still be set below." when the step is shown. A recorded reject or return whose reason the page cannot read sets `offerNotice` false at open: no Notify, no draft, no "No email is sent" line; the intro keeps "nothing can be changed". | "F2": the intro (pure, with must-not-fire), a recorded reject shows the new intro with Notify, an unreadable recorded reason offers none and asks for no draft. |
| K: a draft still in flight from an earlier opening could settle the next opening's loading entry. | `useNoticeDrafts.ts`: a generation counter bumped by `reset`; an answer from an older generation is dropped. Nothing resets the wizard's, so it is unchanged. | "K": cancel while the draft is out, reopen, the old 429 arrives, the new opening is still loading and then gets its own draft. |

Behaviour change to an earlier test: 132ui's "a 400 notice_refused on the banner is shown in the form" (a notice kept from the wizard, refused on the banner repeat) used to expect the read-only refusal bar; it now expects the refusal under the editable Notify fields (the old bar remains for a form that does not offer Notify).

After round 1: `npx jest` 18 suites, **653 passed, 0 failed** (645 after the first commit; +8 round-1 tests); `npm run build` passes (surface gate 0 errors).

Failing before, passing after (round 1): with `DecideInvoiceAction.tsx`, `useNoticeDrafts.ts` and `decisionModel.ts` put back to `d206838a2a`, the round-1 tests gave 7 failed, 1 passed (the must-not-fire test); with the fixes all pass.

## Known limits

1. **K: a repeat from another tab.** A stranded decision first sent from a different tab or browser has no notice in this page's memory either, so the form offers Notify here; if the reviewer does not fill it in here (turns it off), that repeat carries none. This is the offering, not a guarantee: option (b) (the server stores the first attempt's screened notice) is the only way a repeat could carry the first attempt's notice without the reviewer re-confirming. Not done: new server storage.
2. **The reviewer re-confirms, the first confirmation is not recovered.** After a reload the draft is the server's fresh draft; any edit made before the reload is lost (it lived only in memory).
3. **A repeat that answers `alreadyDecided`** (another approver finished it meanwhile) enqueues no notice on the server (`AlreadyDecided` is never enqueued); the form cannot tell this beforehand.
4. **No Skip.** The wizard's Skip is the shell's; the form's equivalent is the switch (same effect: no notice fields).
6. **The form grows when Notify appears late.** A refusal of a notice kept from the wizard offers Notify in a form that opened small; the form's size is fixed when it opens, so the fields show in the small size until it is closed and reopened (a reopen asks for a fresh draft, as any opening does).
7. **A refusal unlocks the reason and note** (not in the recorded stage), the same as the wizard's round 2: the refused request provably decided nothing, so the typed reason may be changed. The 400 comes before the service runs; no unsure outcome can hide behind it.
8. **The stale-draft guard is per opening.** Retry within one opening is a new request of the same generation; two quick Retry clicks are not de-duplicated beyond the step's own button state.
9. The form's `WizardContext` is built from the form's state so the unchanged `NotifyStep` can read it; only `form`, `readOnly`, `loadNotice` and `patchNotice` are used by the step.
