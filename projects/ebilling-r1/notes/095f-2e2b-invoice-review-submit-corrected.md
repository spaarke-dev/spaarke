# 095f-2e2b: Invoice Review "Submit corrected invoice" (EB-D-243, EB-D-245, option B')

Task: `projects/ebilling-r1/tasks/095f-2e2b-invoice-review-submit-corrected.poml`. Design: `notes/095f-part2-decision-notes-design.md` sections 13 and 16. Server contract: `notes/095f-2e2a-internal-route-correction.md` (the "Form-field contract" table).
Branch: `task/ebilling-095f-2e2b`, from `work/ebilling-r1` at 00f06d7c40. Rigor FULL (shared UI library used by the external SPA, plus two code pages).
Client only: no server change, no deploy.

## What changed

| Where | Change |
|---|---|
| `Spaarke.UI.Components` `InvoiceSubmission/InvoiceSubmissionWizard.tsx` | New optional prop `correction?: InvoiceCorrectionTarget`. With it: the record is the invoice's own (fixed, no Record step), the firm is the invoice's own (no firm step, `loadFirms` is not called), the firm invoice number is prefilled and read-only, the Details step has a required Note (at most 2,000 characters, no control character but line break and tab, no `maxLength`: it counts and says so), and `submit` gets `firmId` and `correction: { invoiceId, note }` (note trimmed). A second Submit while one is in flight joins the request in flight, so a double click sends one Correction (095f-2e2a K2). Without `correction` nothing changes (below). |
| `InvoiceSubmission/types.ts`, `messages.ts`, `index.ts` | `InvoiceCorrectionTarget`, `InvoiceSubmissionCorrectionInput`, optional `InvoiceSubmissionInput.correction`, `InvoiceSubmissionError.reason`, three code constants, and the plain sentences for a Correction's refusals (table below). All additive. |
| `InvoiceSubmission/correctionNote.ts` (new) | The note rules (limit, control characters, counter, required). Same limit and rule as Invoice Review's `services/noteModel.ts` (a copy, not an import: that file is in a code page, this one in the shared library). |
| `CreateInvoiceWizard/internalInvoiceSubmission.ts` | `submit` sends `mode=Correction`, `invoiceId`, `correctionNote` (each once) when `input.correction` is set, and none of the three otherwise. New `loadCorrectionPrefill`, `readCorrectionPrefill`, `correctionTargetFromPrefill`, `correctionPrefillErrorMessage`. For a Correction only: a 404 is "not available to you, or gone", a 400 ValidationProblem shows its first field sentence, and a 409's `reason` is kept on the error. |
| `CreateInvoiceWizard/CreateInvoiceWizard.tsx` | New optional prop `correctionInvoiceId`. Set: reads the correction prefill, then mounts the shared wizard with `correction` (own component `CorrectionHost`, so the new-invoice path keeps its hooks and markup). A refusal shows the plain reason and Close (and Try again for a failed read). |
| `src/solutions/CreateInvoiceWizard/src/main.tsx` | Passes `correctionInvoiceId` from the launch `data` (the same `parseDataParams` envelope that carries `entityType` / `entityId` for `lockAssociation`). |
| `src/solutions/InvoiceReview` | `SubmitCorrectedInvoiceAction` (new), wired into the `invoiceActions` slot by `InvoiceReviewWithDecisions` (new optional prop `openCorrectionWizard`; absent means no action). `decisionApi.getCorrectionPrefill`. `main.tsx` opens the code page `sprk_createinvoicewizard` with `correctionInvoiceId=...` through `createXrmNavigationService().openDialog` (60% by 70%, as the other wizard launches). |
| Versions and lockfiles | `Spaarke.UI.Components` 3.0.0 to **3.1.0** (`package.json`, own lockfile). Lockfiles that carry its version: `external-spa` and `InvoiceReview` (2.4.0 to 3.1.0 by `npm install`), and seven that pinned 3.0.0 (`Reporting`, `SpaarkeAi`, `LegalWorkspace`, `Spaarke.DailyBriefing.Components`, `Spaarke.AI.Widgets`, `Spaarke.Compose.Components`, `Spaarke.Communication.Components`) with the one version line. `CreateInvoiceWizard`'s lockfile has no entry for the library (it reaches it by a Vite alias), so it is unchanged. Many other lockfiles pin 2.x already; they are not this change's consumers and were left. |

## How the user gets there

1. Invoice Review, an invoice that is In Review: **Submit corrected invoice...** (footer, beside Decide invoice). It shows for no other status, and not at all when the host passes no launcher.
2. The page reads `GET /api/finance/invoices/{id}/correction-prefill` first. A refusal the server already knows (below) is a plain sentence at the top of the page and no dialog opens.
3. The code page opens as a dialog with `correctionInvoiceId`. It reads the same prefill, shows "Submit corrected invoice", and the user picks the corrected PDF (File step), then sees the invoice number (read-only), the optional PO number and the required Note (Details step).
4. Submit posts the multipart Correction; the result screen is the existing one (Processing, Accepted, or the held panel).
5. When the dialog closes the page reads the invoice again. `confirmed: false` (never produced by the platform close today, see known limits) would skip that.

Why the page reads the prefill itself, and the wizard again: the page can say "this one has moved on" without opening a dialog, and the wizard (a separate code page) must not trust a launch parameter for the number, firm or record. The server checks everything again when the PDF is sent.

## Composition (spa-r4 condition 2)

- **`correction` + `lockedRecord`: supported.** `correction` fixes the record to `correction.record` by itself, so `lockedRecord` is not needed. If both are given, `lockedRecord` names the record (it wins, as it wins over `initialRecord`); the wizard's locked-record check then looks for that record in `loadRecords`, and the server refuses a record that is not the invoice's own (409 `different_core_record`). Tested both ways.
- **`correction` + `hideTitle`: supported.** No title bar and no second close button; the note box and Submit work. Tested.
- **`correction` + the `submittingRef` close guard: supported.** The title-bar close is ignored while the Correction is in flight, the refusal that follows is shown, and the close works once it settled. The success screen's Done keeps the raw `onClose`. Tested.
- **`correction` ignores `loadFirms`**: the invoice keeps its firm (the server refuses another), so there is no firm step even when the host's `loadFirms` would offer several.

## Without `correction` (spa-r4 condition 1)

Every existing suite passes unchanged, including `InvoiceSubmissionWizard.lockedRecord.test.tsx`, `InvoiceSubmissionWizard.test.tsx` and `CreateInvoiceWizard.test.tsx`. New must-not-fire tests: no note box, an editable number, the same hint, `loadFirms` called, `submit` input has no `correction` key (and no `firmId` for a single firm); the multipart form of a new invoice has no `mode`, `invoiceId` or `correctionNote`; a 404 and a 400 ValidationProblem of a new invoice keep today's words; the wizard of `CreateInvoiceWizard` without `correctionInvoiceId` reads nothing from the prefill route; Invoice Review shows no action for any status but In Review, nor without the launcher.
The in-flight guard is applied to a Correction only: a new invoice is not guarded (the shell already disables its button once it has re-rendered; the guard closes the gap before that).

## Refusals and their words

The server's own `detail` always wins; the words below are for an answer that carries none (and are the words the page and the wizard both show for a refusal the server states in codes).

| Where it arises | Server answer | What the user reads |
|---|---|---|
| Wizard, Submit | 400 `ebilling.submission.correction_note_required` | Say why this invoice is corrected. A correction needs a note. Nothing was saved. (The Submit button is off without a note, so this is the backstop.) |
| Wizard, Submit | 400 ValidationProblem (`errors.mode`, `invoiceId`, `correctionNote`) | The server's first field sentence, for example "correctionNote may be at most 2000 characters." |
| Wizard, Submit; page and wizard, prefill | 404 (uniform; `reasonCode`, no `errorCode`) | This invoice is not available to you, or no longer exists, so it cannot be corrected. Nothing was saved. |
| Wizard, Submit | 409 `correction_not_allowed`, `reason` `invoice_status` | Only an invoice in review can be corrected; this one has moved on, so nothing was saved. |
| same | `different_firm` | This invoice belongs to another firm, so this submission cannot replace it. Nothing was saved. |
| same | `different_core_record` | The invoice to correct is filed under another record, so this submission cannot replace it. Nothing was saved. |
| same | `different_invoice_number` | A corrected invoice keeps the firm invoice number of the invoice it replaces. Nothing was saved. |
| same; prefill `canCorrect` false | `no_submission` | This invoice has no finished submission to correct, so nothing was saved. |
| same; prefill `canCorrect` false | `submission_in_progress` | This invoice is still being processed. Try again when it has finished. Nothing was saved. |
| same | a reason a newer server adds | This invoice cannot be corrected right now. Nothing was saved. |
| Wizard, Submit | 503 `ebilling.submission.record_unavailable` | This record is temporarily unavailable. Try again shortly. (The shared table's existing words; the server's own detail names the invoice.) |
| Page and wizard, prefill | 503 or no answer | This invoice could not be read right now. Try again shortly. Nothing was saved. (The wizard also offers Try again.) |
| Page and wizard, prefill | 200 with no firm, no number or an unknown record kind | This invoice cannot be corrected from here: its firm, number or record is not on file. Nothing was saved. |
| Wizard launch | `correctionInvoiceId` is not a GUID | The invoice to correct could not be identified. Close this and open it again from the invoice. |
| Page, opening the dialog | the dialog launch throws | The corrected-invoice form could not be opened. Try again. Nothing was saved. |

After a refusal at Submit the wizard stays on Details with the note and the number kept, and Submit works again (the in-flight guard is released).

## Tests

All new test files are named for the task or the correction:

- `Spaarke.UI.Components`:
  - `InvoiceSubmission/__tests__/InvoiceSubmissionWizard.correction.test.tsx` (25): what is fixed, the note rules, one request at a time, each refusal (10 cases plus the server-wins case), composition, must-not-fire.
  - `InvoiceSubmission/__tests__/correctionNote.test.ts` (5): the note rules.
  - `CreateInvoiceWizard/__tests__/internalInvoiceSubmission.correction.test.ts` (21): the multipart fields (each once; none for New), refusals thrown and returned, the prefill read and its shapes.
  - `CreateInvoiceWizard/__tests__/CreateInvoiceWizard.correction.test.tsx` (9): the launch end to end through the real adapter and wizard, the double click posting once, each launch refusal.
- `InvoiceReview`: `__tests__/submitCorrectedInvoice.test.tsx` (28): shown only In Review (8 other statuses), opens once and reads again, back-out, double click, each refusal, dark mode, the prefill client.
- Test support: `testKit.ts` (`correctionPrefill`, the mock's `getCorrectionPrefill`) and `uiComponentsTestSeam.ts` (the shared words).

**Fail-before** (the new test files run against the base source of the ten changed files, ts-jest type diagnostics off for the library so tests, not suites, are counted): library **49 failed, 11 passed** of 60 (the 11 are the must-not-fire cases, and the note rules whose file is new); Invoice Review **19 failed, 9 passed** of 28 (the 9 are the must-not-fire cases). Removing the in-flight guard fails the double-click test (2 calls instead of 1), proving the guard is what holds it.

**Results after**: see the task report (counts per suite).

## Known limits

- **K1, the dialog result cannot tell Done from Cancel.** `INavigationService.closeDialog` always reports `confirmed: true`, and the platform close button resolves with nothing (treated as confirmed). So the page reads the invoice again after any close. That read is harmless; but a Correction is committed by a job after "received", so a read right after Done can still show In Review. The wizard's result screen polls until Accepted before most users press Done, and Reload shows the superseded state.
- **K2, two Corrections of one invoice from two places** (two tabs, or two users) can still both be received; the second fails at commit (095f-2e2a K2). The wizard only closes the double click of one wizard.
- **K3, an invoice with no firm** cannot be corrected here: the internal route needs the firm named, and the server's check for a firm-less invoice answers `different_firm` (095f-2e2a K6). The page says so before opening the wizard.
- **K4, the prefill is read twice** (the page, then the wizard). They can disagree only if the invoice changes between the two reads; the wizard's read decides what is prefilled and the server checks again at Submit.
- **K5, the note limit is written in two packages** (`correctionNote.ts`, and Invoice Review's `noteModel.ts`). Invoice Review's `serverWordsPin.test.ts` pins its copy to the server source; the library's copy has no such pin. A change to the server limit must change both.
- **K6, no Jest suite or typecheck script in `src/solutions/CreateInvoiceWizard`** (none before this task either). Its one line is covered through the library's `CreateInvoiceWizard` tests; `npx tsc --noEmit` there fails on the base with the two `@spaarke/ui-components/...` alias imports it cannot resolve (Vite resolves them); with the alias mapped, the changed line type-checks (one other error, on `authenticatedFetch`, is on the base).
- **K7, 503 at Submit** uses the shared table's "This record is temporarily unavailable" words when the server sends no detail; the server writes a detail for it.
- **Seen, not changed (scope):** a new invoice's double click is not guarded (the shell re-enables its button when a second `onFinish` that returned nothing settles; the close guard keeps that from closing the wizard). Worth a separate task for the new-invoice path, which spa-r4's condition 1 keeps out of this one. Also: `finishAsOperator.test.tsx` of Invoice Review failed once in a full run under load and passed alone and on re-run (its config notes the 5 s cold-cache risk).

- **K8, stale 2.x lockfiles elsewhere** pin an older library version in their own lockfile. That is metadata only (the version line of a linked local package; the review agreed). Left as they were: they are not consumers of this change.
- **K9, `confirmed: false` is unreachable today.** The host's dialog close always reports `confirmed: true` (or resolves with nothing, treated the same), so the page's "back out, do not reload" branch (`result.confirmed === false`) cannot be taken with this launcher. It stays because the launcher's type allows it and it is the correct behaviour if the host ever reports it; its test drives it with a launcher that returns `confirmed: false`.
- **K10, a second correction after Done (the K2 case, from one reviewer).** After Done the page reloads, but the replacement commits asynchronously, so for a short while the invoice can still show In Review and the action is still offered. A second Correction sent in that window is received (the server's 095f-2e2a K2: the second fails at commit, nothing is lost, but the reviewer sees a failure later). The action does not hold the button off after a Done: it has no server signal for "a correction is in flight" besides the prefill's `submission_in_progress`, which it does read first.

## Review round 1 (Opus) fixes

- **F1, view-only callers.** The action takes `canWrite` (the page's own `reviewAccess`: Write on the invoice, In Review) and renders nothing without it, as `DecideInvoiceAction` does. Before, a caller who could only view saw an enabled button and got the uniform 404.
- **F2a, the 404 sentence.** The server's 404 carries the generic detail "The requested record was not found.", which won over the correction's own sentence, so that sentence never showed. For a 404 the correction paths now ignore `detail` (the adapter's `fromProblem`, so the wizard's Submit and prefill read, and the page's `correctionReadFailureMessage`). Tests now give the 404 the real detail and assert the correction sentence.
- **F2b, control characters.** `correctionNote.ts` now matches `char.IsControl`: U+0000 to U+0008, U+000B, U+000C, U+000E to U+001F, and U+007F to U+009F (so U+0085 is refused); line break and tab stay allowed.
- **F2c, one prefill type and reader.** Invoice Review no longer has its own `CorrectionPrefill` type or weak validator: it imports the library's type and validates with `readCorrectionPrefill(b) !== null`. The reader now also refuses a body with no string `firmInvoiceNumber` (the server's DTO always sends it; before, it silently became an empty number). On the page, a 2xx body that is not a prefill reads as "could not be read, so it cannot be corrected" (the client's own "unexpected body" detail is not shown for it).
- **K fixes.** The `correction` prop comment says a second Submit joins the request in flight; the `MockDecisionApi` spacing; the dark-mode test now asserts the provider carries the dark tokens (and differs from light) and that the button has no inline colour.
- **Builds run:** Invoice Review `npm run build`, and the Create Invoice wizard solution's build (see the task report for results and any failure that is already on the base).

## Fix to iss034b's test: `finishAsOperator.test.tsx` flake

Not part of 2e2b's behaviour; the file is iss034b's and flaked in several full-suite runs here (never when run alone).
- **Cause (found, not a timeout).** Every async wait in the file is already on the file's 5 s `configure({ asyncUtilTimeout })` (it is module-level, so it covers `findBy*` and `waitFor` in every helper; none is on the 1 s default). The failing line was a SYNCHRONOUS `screen.getByRole('dialog', { name: title, hidden: true })` in the "re-read shows the hold gone" describe, right after the banner went. With the banner gone the open form's surface comes to carry `aria-hidden="true"` (Fluent's focus handling, once the trigger inside the banner is removed), and an `aria-hidden` element's accessible name is empty, so a query by name misses a form that is still open. It reproduced in 6 of 8 parallel runs of that describe (instrumented) and 1 of 6 (not). The form's own state stayed `open: true` throughout, so this is not a lost form in the page's state.
- **Fix.** The three checks that depended on the name now use the element: the sync check is `expect(anyDialog()).toBe(dialog)` plus the title text inside it, and the negative "form is gone" waits (which could pass early on an empty name) use the role-only `anyDialog()`. The opening waits (`dialogOf(title)`, by name) are unchanged: they run before the surface is hidden and keep covering the accessible name.
- **Possible product issue, not verified in a browser:** if the same `aria-hidden` appears in a real browser, a screen reader would lose the open form when the hold clears under it. jsdom cannot tell. Filed as an observation for the operator, not changed here (iss034b's component, outside this task).
- **Proof:** the describe, 8 parallel runs: 0 failed (was 6 of 8 failing). Full Invoice Review suite (15 suites, 530 tests): 3 runs in a row with default workers, 3 of 3 green, and one `--runInBand`, green.

## Gates

Typecheck, build and test results are in the task report. Pre-existing on the base: Invoice Review's `tsc-surface-gate` reports 48 errors in shared libraries (none in this change, none surface-owned); `src/solutions/CreateInvoiceWizard` has no typecheck script (K6).
