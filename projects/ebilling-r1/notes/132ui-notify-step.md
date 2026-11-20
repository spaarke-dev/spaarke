# Task 132ui: Invoice Review Notify step (the notice to the firm in the decision wizard)

Branch `task/ebilling-132ui`, from `e11be217cc` (`work/ebilling-r1`). Rigor FULL. Model: Sonnet @ high. EB-D-258; owner question 2 of `132-ebilling-notifications.md` ("confirm 132ui covers both routes": it does, Reject asks `InvoiceRejected` and Return asks `ReturnedToFirm`).

Client-only (`src/solutions/InvoiceReview`). No server, schema, shared-library, external SPA or `.claude/` change. Nothing was deployed or pushed to `work/ebilling-r1`.

## What changed

| File | Change |
|---|---|
| `src/services/decisionApi.ts` | `getFirmNoticeDraft(invoiceId, kind)` (`POST .../firm-notice/draft?kind=`, same `call` / `ApiResult` pattern); `reject` and `returnToFirm` take an optional 4th argument `notice`; the body gets `noticeSubject` and `noticeProse` (trimmed) only when both have text, never empty strings. Types `FirmNoticeKind`, `FirmNoticeDraft`, `FirmNotice`; route `decisionRoutes.firmNoticeDraft`. |
| `src/services/noticeModel.ts` (new) | The pure rules: kind of an outcome, limits (subject 120 not counting the tracking suffix the server appends, prose 1500, both trimmed), `noticeBlocker` (what holds Next), `noticeToSend` (the one rule of what the request carries), lock helpers, `isNoticeRefusal` (400 `ebilling.notice_refused`). |
| `src/components/decision/NotifyStep.tsx` (new) | The built-in step: spinner while the draft loads; on failure the message and code with Retry; "Send this notice to the firm" (on once a draft loaded); Subject and Message with `n / limit` counters (no `maxLength`, nothing is cut); the source line (AI draft or standard wording); the "table, amounts and clause quotes are added by Spaarke" line; "Restore draft"; the server's refusal under the fields; a locked caption. No HTML preview and no `dangerouslySetInnerHTML`. |
| `src/components/decision/InvoiceDecisionWizard.tsx` | The slot (`NotifyStep` type, `notifyStep` prop) is replaced by the built-in step on Reject and Return of an editable wizard. Holds `loadNotice` (once per kind per opened wizard, guarded by a ref) and `patchNotice`; reads Skip from the shell's step state; `onFinish` passes the notice only when sent on purpose, handles the notice refusal like the note refusal (stays open), and the unsure-outcome lock carries the notice and the send choice (and fixes the form's entry to what was sent). |
| `src/components/decision/wizardContext.ts` | `form.notices` (one entry per kind), `form.noticeError`, ctx `loadNotice` / `patchNotice`. |
| `src/components/decision/decisionLocks.ts` | `WizardLock.notice?: { send, subject, prose }`. |
| `src/components/decision/steps.tsx` | `ConfirmStep` says whether the firm is emailed this notice or gets no email (`confirm-notice`), and shows the server's refusal with the way back (`confirm-notice-refused`). |
| `src/components/decision/DecideInvoiceAction.tsx`, `src/components/InvoiceReviewWithDecisions.tsx` | The `notifyStep` slot prop chain removed (nothing else used it). |
| `src/__tests__/*` | `firmNoticeStep.test.tsx` (new, AC tests), `noticeModel.test.ts` (new), `decisionApi.test.ts` and `serverWordsPin.test.ts` extended, `testKit.ts` (`firmNoticeDraft`, `getFirmNoticeDraft` mock), `InvoiceDecisions.test.tsx` (the slot tests replaced by an AC1 test; reject / return paths skip the new step), `noteBoxesDecisions.test.tsx` and `noteReviewRound1Decisions.test.tsx` (their Reason to Confirm paths now pass through Notify, Back counts adjusted). |

## How it behaves

- Reject and Return show **Notify firm** after Reason. Approve and the read-only (decided) wizard never do, and ask for no draft.
- The draft is asked for when the step is first shown, once per kind per opened wizard. Each kind keeps its own draft, edits and send choice; the form holds them, so leaving the step does not lose them.
- Next is held while the draft loads (so the Confirm wording cannot change under the reviewer), and for a blank or over-limit notice with sending on. A failed draft, or sending off, holds nothing. Skip always works.
- Skip is read from the shell's own step state (`handle.state.steps`), captured when the Confirm step renders; Back to the Notify step and Next again is not a skip.
- Sent only when: draft loaded, "Send this notice" on, step not skipped, text valid. Then the call is `reject(id, reason, note | undefined, { subject, prose })` (trimmed). Otherwise it is exactly the old call with no extra argument.
- A 400 `ebilling.notice_refused` keeps the wizard open, shows the refusal on the Confirm step (with the way back) and under the fields on the Notify step, re-reads nothing, locks nothing; any edit or toggle clears it.
- An unsure outcome (503 saved in part, or no answer) records `notice: { send, subject, prose }` in the page-level lock and fixes the form's entry to what was sent: the reopened wizard shows the same notice and choice, read-only, and Skip does not drop a locked notice. A draft that lands after the lock is ignored.

## Acceptance criteria to tests

Tests are in `src/__tests__/firmNoticeStep.test.tsx` unless named.

| AC | Test |
|---|---|
| AC1 | `AC1: where the Notify step shows` (Approve path has no step and asks for no draft; a decided wizard shows none); `InvoiceDecisions.test.tsx` "task 132ui AC1: Reject and Return each show the Notify step after Reason; Approve never does" |
| AC2 | `AC2: ... once per kind` (not before the step is shown; kind matches; no repeat on Back and Next; each kind keeps its own edits across switching; a new opening asks again); `decisionApi.test.ts` (route and kind in the query) |
| AC3 | `AC3 / AC4: ...` (sending on carries the edited trimmed text; Return; sending off carries nothing, 2-argument call; Skip carries nothing; Skip is not remembered after Back and Next); `decisionApi.test.ts` (body keys absent, never empty strings, trimmed); `noticeModel.test.ts` (`noticeToSend` fire and must-not-fire cases) |
| AC4 | the same block (`confirm-notice` says "will be emailed this notice" or "No email is sent to the firm", for on, off, skipped, failed draft) |
| AC5 | `AC5: ...` (refusal under the fields and on Confirm, no success screen, nothing re-read, edit then finish again; turn the notice off after a refusal; nothing locked; another 400 stays the shell's alert) |
| AC6 | `AC6: ...` (409, 503 x2, 429 and no-answer show message and code with Retry and finish without a notice; Retry; half-loaded draft: Next held, Skip sends none, a late draft changes nothing) |
| AC7 | `AC7: ...` (subject 121 holds and 120 does not, suffix not counted; prose 1501 and 1500; blank; nothing cut off; must-not-fire with sending off) |
| AC8 | `AC8: ...` (notice on: reopened locked with the same text and choice, repeat is the same request, Skip does not drop it; notice off or skipped: locked off; a late draft ignored; must-not-fire: a 503 that saved nothing locks nothing) |
| AC9 | `AC9: no unsafe HTML` (the scan has fire controls for `dangerouslySetInnerHTML`, `innerHTML =`, an iframe without an empty sandbox; the page has none; the draft's `html` is never rendered) |
| AC10 | the rest of the package passes; `npm run typecheck` clean |
| Contract | `serverWordsPin.test.ts`: limits 120 and 1500, the code `ebilling.notice_refused`, the draft route and kind names, the draft response members, and `InvoiceDecisionRequest` now `Reason, Note, NoticeSubject, NoticeProse` |

## Results

- **Package tests** (`npx jest` in `src/solutions/InvoiceReview`): 18 suites, **616 passed, 0 failed**. Baseline on `e11be217cc`: 16 suites, 562 passed, 1 failed (`serverWordsPin`, see limit 7).
- **tsc**: `npm run typecheck` (the surface gate) reports Surface-owned: 0 errors; the 99 shared-library errors are the pre-existing ones it ignores.
- **client-lint**: InvoiceReview is not a client-lint package (see limit 6), so `client-lint.mjs check --package ...` has nothing to run for it; the package has no ESLint config either.
- **Failing before, passing after (AC3 and AC5).** The "before" behaviour (the wizard never sends a notice; a 400 `ebilling.notice_refused` falls to the shell's error bar) was reproduced by temporarily editing `InvoiceDecisionWizard.tsx` (the notice forced to null, the refusal branch disabled), then restoring it. `npx jest src/__tests__/firmNoticeStep.test.tsx -t "AC3|AC5"`:
  - with the old behaviour: **6 failed, 3 passed** (failing: AC3 "Reject with sending on carries exactly the edited, trimmed subject and message", AC3 "Return with sending on carries the Return draft", AC3 "going Back ... after a Skip ... sends the notice", AC5 "shows the refusal under the fields ... edit and finish again", AC5 "turn the notice off after a refusal", AC5 "a refused notice is not an unsure outcome"; the 3 that pass are the negative cases that hold on the old behaviour too: sending off, skipped, and "another 400 stays the shell's alert");
  - with the change: **9 passed, 0 failed**.

## Known limits and things noticed

1. **No HTML preview.** The server's `html` is not rendered (allowed by the task); a preview would be a sandboxed iframe only.
2. **The "Finish decision..." banner repeat sends no notice.** `IncompleteDecisionBanner` (ISS-032) repeats a stranded reject or return with the reason and note only. If the wizard's request carried a notice and came back unsure, finishing it from the banner sends no firm email (the wizard reopen path is exact). Not changed here (out of scope); worth a follow-up so a stranded decision can carry the locked notice.
3. **The tracking suffix is a heuristic.** The subject counter excludes the draft's trailing ` (...)` group, as the server removes the exact ` (tracking)` suffix before it counts. The server stays the authority.
4. **The refusal cannot move the wizard.** The shell has no go-to-step, so after a refusal the Confirm step shows the message and says to go Back; the Notify step shows it under the fields.
5. **A locked reopen has no draft to restore to** (no source line, no Restore draft); the text and choice are shown as sent.
6. **Lint.** InvoiceReview is not a client-lint package (`PACKAGES` in `scripts/quality/client-lint/eslint.client.config.mjs` does not list it) and the package has no ESLint config, so only `tsc` (the surface gate) applies.
7. **Pre-existing red, fixed here.** `serverWordsPin.test.ts` was failing on `e11be217cc` (the server's `InvoiceDecisionRequest` gained `NoticeSubject` and `NoticeProse` in 132, the pin still expected two members). Updated to the real shape.
