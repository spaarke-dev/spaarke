# 164b: the Add line client (Invoice Review code page)

Branch `task/ebilling-164b` (from `work/ebilling-r1` at `e61da0e2f7`). Scope is the client only (task 164, step 164b; brief section 3 client, R19, section 8 C1-C4). No server change, no ribbon (164c), no external projection.

## What landed

- `services/invoiceReviewApi.ts`
  - `ReviewLine.reviewerAdded` (the server's `ReviewLineDto.ReviewerAdded`).
  - `AddLineRequest` / `AddLineResponse`, `routes.addLine` (`POST /api/finance/invoices/{id}/lines`), `InvoiceReviewApi.addLine`.
  - The Add line codes, `addLineFailureMessage(failure)` (code to user sentence; falls back to the existing `writeFailureMessage`) and `staysInAddLineForm(failure)` (which refusals leave the form open).
- `services/reviewModel.ts`
  - `canAddLine(review, refusedWith403)` = `reviewAccess(...).canWrite` (`flags.canDecide` AND header status `InReview`, and false once a write was refused with 403 this session).
  - `AddLineForm`, `EMPTY_ADD_LINE_FORM`, `buildAddLineRequest(form, requestId)` (typed text to wire types only; the fields of the other cost type are never sent), `REVIEWER_ADDED_LABEL` / `_MEANING`.
- `components/Dialogs.tsx`: `AddLineModal` on `FormModal` (ADR-050): Fee / Expense, date, timekeeper, hours, rate, amount, task / activity (Fee) or expense code (Expense), description, reason. The submit is never disabled by a client rule.
- `components/InvoiceReviewPage.tsx`: the "Add line…" button in the lines toolbar (only when `canAddLine`; disabled while busy or `firmNeeded`), `openAddLine` (makes the `requestId`), `submitAddLine`. `runWrite` gained two optional options (`failureText`, `staysInDialog`); the existing callers pass neither and behave as before.
- `components/LinesGrid.tsx`: an "Added" badge (accessible name "Added by reviewer", tooltip) in the Status cell where `reviewerAdded` is true.

## Server contract used (checked against 164a, no difference from the brief)

- Request body: `requestId, costType ("fee"|"expense"), date, timekeeper, hours, rate, amount, taskCode, activityCode, expenseCode, description, reason`. Nothing names a person, total or status.
- 200: `{requestId, lineId, decisionId, status, alreadyApplied, totals}`.
- 400 `ebilling.line_add.{invalid, amount_mismatch, code_not_allowed}`, `ebilling.line.{reason_required, reason_too_long}`, `ebilling.decision.invalid`.
- 409 `ebilling.line_add.{firm_missing, code_set_unavailable, procedure_gap, id_reused, changed}`, `ebilling.invoice.{not_in_review, totals_refused, busy}`, `ebilling.line.corrections_pending`.
- 500 `ebilling.decision.owner_unresolved`; 503 `ebilling.decision.incomplete` with `failedStep` (the existing `isIncomplete` already matches it).

## requestId

One `requestId` (`newDecisionId()`) is made when the form opens and held in page state with the open form.

- A refusal that leaves the form open (400s, and the 409s in `staysInAddLineForm`, status 0, 5xx) is resent as typed with the same id: nothing was written, so it is safe.
- A 503 incomplete closes the form (the line is saved) and the page offers Repeat. Repeat re-runs the same closure: the same request, the same id.
- Closing the form and opening it again makes a new id.
- If a send got no answer (status 0) and the reviewer then EDITS the form and resends, a line that did commit gives 409 `id_reused`; the page closes the form and offers Reload so the reviewer sees the line.

## Deviations

- The task says "vitest"; the InvoiceReview page runs **jest** (`npm test` = `jest`, ts-jest). The tests are jest.
- Build script: the page has no `build:prod`; `npm run build` is used (CLAUDE.md 12's `build:prod` rule is for PCF).
- Button visibility uses `reviewAccess(...).canWrite`, which is `canDecide` AND In Review, plus the existing "a 403 turns the page read-only" rule.
- The client parses typed number text to numbers. Text that is not a number cannot be sent as one, so the form names it ("Hours must be a number") and sends nothing; every rule the server owns is the server's answer.
- Tests are C1-C4 only. Within C1/C2/C3 there are several `it` cases (retry paths, an Expense, one case per server code in C2, the status list in C3); no other test areas.

## Tests (C1-C4)

`describe('InvoiceReviewPage: Add line (task 164b)')` in `src/__tests__/InvoiceReviewPage.test.tsx` (22 cases); `testKit.ts` gained `reviewerAdded: false` on `line()`, `addLine` on `mockApi` and `ADDED_LINE_ID`.

| Id | Proves | Cases |
|---|---|---|
| C1 | One requestId per opened form; the same one on Repeat after a 503 incomplete (identical body); the same one when a refusal leaves the form open and it is resent; a newly opened form gets a new id; the body holds only typed values plus `costType` and `requestId`; an Expense sends no Fee fields; on success the form closes, the page re-reads and the new line is selected | 4 |
| C2 | Each server code shows its sentence in the form (400 invalid / amount_mismatch / code_not_allowed / reason_required; 409 firm_missing / code_set_unavailable / procedure_gap / corrections_pending / totals_refused / busy / changed; 500 owner_unresolved), with the typed values kept and no Reload; text that is not a number is named and nothing is sent; `id_reused` and `not_in_review` close the form and offer Reload; a 403 turns the page read-only | 15 |
| C3 | The button shows only when `canDecide` AND In Review (hidden for canDecide false and for each of Approved, Rejected, Returned to Firm, Sent to AP, Paid, Cancelled); disabled while `firmNeeded` | 2 |
| C4 | The badge on a `reviewerAdded` line only (accessible name "Added by reviewer") | 1 |

Results:

- Add line tests: 22 failed with the page and grid files at HEAD (before), 22 passed after.
- Whole InvoiceReview jest suite: 6 suites, 199 tests, all pass (177 before this task plus 22).
- Production build (`npm run build`: html reset check, tsc surface gate, vite build): succeeds. tsc-surface-gate: surface-owned errors 0 (99 pre-existing errors in shared libs, deferred, unchanged).

## For the owner / next task

- jsdom: after a send, the Fluent dialog surface carries `aria-hidden`, so role queries on it fail. The tests use DOM queries for "no dialog" and `hidden: true` for the submit button. This is the same shared `FormModal` the Correct and Bulk dialogs use; not investigated further here.
- `ReviewLine.reviewerAdded` is a required field. A server older than 164a would omit it, and the badge would then not show (undefined is falsy); nothing else reads it.
- The Add line button is also shown on the By rule tab (it sits in the shared toolbar).

## Review fix round

The adversarial review's fix-now findings and the cheap known limits. Tests are in `src/__tests__/InvoiceReviewPage.test.tsx` (describe "Add line (task 164b)") and `src/__tests__/reviewModel.test.ts`.

| Finding | Change | Test |
|---|---|---|
| F1-1: a failed Repeat lost the requestId and said "Nothing was changed" | `runWrite` takes `isRepeat`, set by the page's own Repeat. A Repeat that fails with status 0, any 5xx or a 409 `busy` / `line_add.changed` keeps `{Repeat}` bound to the SAME closure (same id and body) and shows `repeatFailureMessage` (points back to Repeat, never "Nothing was changed"). Any other refusal on a Repeat keeps its own sentence with the "Nothing was changed" claim removed and offers no Repeat. A Repeat does not close a form the reviewer opened since. Shared by every `runWrite` caller (decisions, bulk, corrections, Add line); first-attempt behaviour is unchanged. New helpers `isRetryableRepeatFailure` / `repeatFailureMessage` in `invoiceReviewApi.ts` | Add line Repeat -> 409 busy -> 409 changed -> success (one requestId, identical body, no form reopened); Repeat -> status 0 -> 500 -> same; Repeat -> 409 totals_refused -> sentence without the claim and no Repeat; decision write Repeat -> 409 busy -> Repeat -> same decisionId |
| F1-2: `corrections_pending` hid the server's text | `addLineFailureMessage` returns `f.detail ?? <fixed sentence>` | The C2 row now asserts the server detail is shown; a second case asserts the fixed sentence when there is no detail |
| F1-3: a first send with no answer said "could not be sent" | `addLineFailureMessage` default branch: status 0 gives "No answer came back, so the line may already be added. Send it again from this form (it is never added twice), or reload first." The form stays open with the same requestId | Status 0 -> the message, the typed values kept, resend uses the same requestId and closes the form |
| K1: `Number("0x10")` / `"1e3"` accepted | `buildAddLineRequest` tests `/^-?\d+(\.\d+)?$/` (after trim) before converting; the "must be a number" message is unchanged | `reviewModel.test.ts`: refused 0x10, 1e3, 1E3, Infinity, 1,5, .5, 5., +2, --2, "1 2"; accepted 1.5, -2, " 2.5 ", 300, 0; blank is null |
| K4: `staysInAddLineForm` comment versus use | `runWrite` consults it only for a 409 (other statuses already stay in the form there). The helper is narrowed to the 409 codes and its comment says so. No behaviour change | The existing C2 rows (all statuses) still pass |
| K3: test gaps | Tests only | 200 `alreadyApplied` is shown as success (line added, selected, form closed); the submit button is disabled while a send is in flight and a second click sends nothing |

### Browser check before UAT (known limits left as notes)

- The "Added by reviewer" badge's `aria-label`.
- `FormModal` carries `aria-hidden` on its surface in jsdom after a send (the tests use DOM queries and `hidden: true`).
- Focus after the form closes.
