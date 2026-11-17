# 095f-2ui — an optional Note box on every Invoice Review decision

Client only (`src/solutions/InvoiceReview/**`). The server side is 095f-2a / 095f-2b; this task sends and shows the note.

## What changed

- `services/noteModel.ts` (new): the one place that knows the note rules. `noteToSend` returns the text as typed or undefined when blank (the trim is for the blank check only). `noteMember` / `noteArgs` add the key only when present, so a blank note never sends `null` or `""` (note is inside the Decision Record hash; `null` would hash differently from an absent key). `noteProblem` / `noteCounterText` give the "n / 2,000" counter and the over-limit message. `isNoteRefusal` / `noteRefusalMessage` read the two 400 codes.
- `components/NoteField.tsx` (new): Fluent v9 `Field` "Note" + `Textarea`, no `maxLength`, counter, field error (server detail under the box).
- `services/decisionApi.ts`: `note` on approve, reject, return, add adjustment, remove adjustment (optional JSON DELETE body `{ note }`), override and blocked return.
- `services/invoiceReviewApi.ts`: `note` on decideLine, decideLines, correctLine, correctHeader (correct and confirm). `acceptClean` is unchanged: decision id only.
- UI: Note on the wizard Confirm step, BlockedBanner, line decision form, bulk dialog, correction dialog, add-adjustment and remove-adjustment forms. "Confirm as read" was one click; it is now a small modal (`ConfirmValueModal`) so it can carry a note.

## Deviation from the POML

No `maxLength` on the Note. A counter (n / 2,000, counted after trimming like the server) and a field error over the limit, with the server's 400 as the backstop (ISS-027). The existing Reason textareas keep their `maxLength` (untouched, out of scope).

## The five client limits, and where each is proved

1. Reason and note survive a 409/503: kept in form state; tests in `noteBoxesDecisions.test.tsx` (wizard, blocked, adjustments) and `noteBoxesPage.test.tsx`.
2. No reload on `concurrent_change`: BlockedBanner and the removal form do not reload on a 409 other than `id_reused` (tests in `noteBoxesDecisions.test.tsx`).
3. Repeat resends the exact request: `runWrite` re-runs the same closure (same decisionId, same note); a new submit with an edited note mints a new decisionId. After an incomplete (503) the wizard and blocked Note boxes lock so the repeat is the exact request.
4. No note on Accept all Clean: `noteApi.test.ts` (wire level) and `noteBoxesPage.test.tsx` (page level) must-not-fire tests; `serverWordsPin` pins `AcceptCleanRequest` to `['DecisionId']`.
5. Removal 409 `ebilling.adjustment.id_reused`: reloads the adjustments list, form stays open with the server sentence and the note; other 409s do not reload.

400 `ebilling.note_too_long` / `ebilling.note_invalid_characters`: field message under the Note box (wizard, blocked, adjustment add/remove, line pane, bulk, correction, confirm), not the generic page error.

## Tests

`__tests__/noteApi.test.ts`, `serverWordsPin.test.ts` (reads the C# source: note codes, the MaxLength=2000 chain, `IdReused`, `Note` on 8 request records, `AcceptCleanRequest`, with control tests for the scan helpers), `noteBoxesPage.test.tsx`, `noteBoxesDecisions.test.tsx`; `InvoiceReviewPage.test.tsx` "confirms a low-confidence value as read" updated for the new modal.

Before (base sources `196930bbbb`, new tests present): 60 failed, 279 passed, 339 total. Failing set: the note tests in `noteApi` (every call), `serverWordsPin` "is the server's AdjustmentCodes.IdReused", most of `noteBoxesPage` and `noteBoxesDecisions`, and `InvoiceReviewPage` "confirms a low-confidence value as read". The accept-clean must-not-fire tests pass at base by design.
After: 339 passed, 339 total (11 suites). Base suite was 256 tests.

## Known limits

- The blocked view's `GET /blocked` returns the reason only, so the note is not shown there.
- Bulk and correction dialogs close on a 409 as before, so the typed reason and note are not kept there; Repeat after a 503 resends the exact request from the closure.
- Add-adjustment `id_reused` does not reload the list (only the removal does).
- Reject/return: the server drops an edited note on a repeat after the record exists (K-1); mitigated by locking the note after an incomplete result.
- Control characters are checked only by the server; the client counts the limit only.
- Existing Reason textareas still carry `maxLength`.
- ESLint is not installed and has no config in this solution, so the `lint` script cannot run (pre-existing).
