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
5. Removal 409 `ebilling.adjustment.id_reused`: reloads the adjustments list. Review round 1: if the re-read shows the adjustment is gone the form closes and the server's sentence stays on the step; if it is still listed the form stays open with the sentence and the note. Other 409s do not reload. An add answered `id_reused` reloads the list too.

400 `ebilling.note_too_long` / `ebilling.note_invalid_characters`: field message under the Note box (wizard, blocked, adjustment add/remove, line pane, bulk, correction, confirm), not the generic page error.

## Tests

`__tests__/noteApi.test.ts`, `serverWordsPin.test.ts` (reads the C# source: note codes, the MaxLength=2000 chain, `IdReused`, `Note` on 8 request records, `AcceptCleanRequest`, with control tests for the scan helpers), `noteBoxesPage.test.tsx`, `noteBoxesDecisions.test.tsx`; `InvoiceReviewPage.test.tsx` "confirms a low-confidence value as read" updated for the new modal.

Before (base sources `196930bbbb`, new tests present): 60 failed, 279 passed, 339 total. Failing set: the note tests in `noteApi` (every call), `serverWordsPin` "is the server's AdjustmentCodes.IdReused", most of `noteBoxesPage` and `noteBoxesDecisions`, and `InvoiceReviewPage` "confirms a low-confidence value as read". The accept-clean must-not-fire tests pass at base by design.
After round 0: 339 passed, 339 total (11 suites). Base suite was 256 tests. After review round 1: see below.

## Review round 1

Base `fbedddfedd`, fix commit `a78f7d19cc` (source and tests together). Fail-before = the new and changed tests run against the round-0 sources (37 failed, 351 passed, 388 total; 4 suites failed). "Fails before" below names the tests that fail at that base; a few `(must-not-fire)` controls also fail there because they need the feature they guard (a test id or a button that did not exist), which is stated in the table.

| # | Finding | Fix | Test (file) | Fails before |
|---|---|---|---|---|
| 1 | A correction or confirm the server found `alreadyApplied` (no note stored) showed "Saved" | `runWrite` takes `warnAfter(value)`; when `alreadyApplied` and a note was sent the notice is a warning: "Already corrected to this value; the note was not saved." / "Already confirmed; the note was not saved." No note sent, or not already applied: the plain success text | `noteReviewRound1Page`: correction, header correction and confirm warn; must-not-fire: already applied with no note (correction and confirm), note on a write made now (correction and confirm) | The 3 warning tests fail; the 4 must-not-fire pass (plain text is today's) |
| 2 | Bulk, correction and Confirm-as-read dialogs closed on any 409 and lost the reason and note | `staysInNoteDialog(f)`: resendable 409s keep the dialog open, with the sentence inside it and the text kept. Every other 409 closes (page Reload notice) and the typed text is kept in page state (`drafts`, keyed by dialog and target) and prefills the next opening of the same dialog for the same target; dropped on success and when the reviewer cancels | `noteReviewRound1Page`: bulk busy / line.pending / concurrent_change stay open and resend; bulk `line.changed` closes and prefills; correction busy stays, `invoice.changed` closes and prefills; header correction prefills; confirm busy stays then `not_in_review` closes and prefills; must-not-fire: other lines, cancel, success, cancel after a stay. `serverWordsPin`: the codes are the server's | All 10 behaviour tests fail; `serverWordsPin` 3 fail (function missing) |
| 3 | A note error showed on the wrong line | `noteFault` carries the line id; the pane shows it only for that line, and `onSelect` clears it | `noteReviewRound1Page`: fault on line 2, select line 4 (and back to 2): none; must-not-fire: it stays while line 2 is open | The select test fails; the stays-while-open control passes |
| 4 | Add adjustment after a lost response (status 0) | Locks like a 503 (read-only form, Repeat, same id); `id_reused` on an add reloads the list; Clear on a locked form reads the list back first and keeps the form locked if the re-read fails, then mints a new id and says the list was re-read. The add id and lock moved from the step (lost on leaving it) to the wizard form | `noteReviewRound1Decisions`: no-answer locks and Repeat resends the same id; `id_reused` reloads; Clear reloads then a new id; Clear with a failing re-read stays locked; Skip then Back stays locked with the same id; must-not-fire: 400/409 not locked, other 409 no reload, Clear unlocked no reload | All fail except the two "must-not-fire" about 409/400; the Clear-unlocked control fails too (no `clear-adjustment` test id at base) |
| 5 | The lock after a partial success was incomplete | An unsure send (503 incomplete, or status 0) locks the REASON with the note, in the wizard (Reason and Confirm steps) and the blocked form, and keeps the locked request at page level (`DecisionLocks` in the `ApprovalProvider`, `decisionLocks.ts`): reopening prefills it, locked, and the repeat is the same request. Cleared on success, when the server state shows the decision done (approval not Pending / hold gone), and when the outcome is changed (wizard) or the other action is opened (blocked). A different outcome after Back is a new request: nothing locked. The invoice decisions have no client decision id (the server derives it), so the lock is outcome, reason and note; the adjustment add's id is locked too (item 4) | `noteReviewRound1Decisions`: reason locked after 503; no-answer locks and reloads; close and reopen prefilled and locked; Back and change outcome unlocks, here and after reopen; blocked reopen for 503 and no-answer, other action unlocked, success clears; must-not-fire: a 503 that saved nothing, a 409 | The reason-locked, no-answer, reopen (wizard and blocked, 503 and no-answer) and change-outcome tests fail; the controls that assert "nothing is locked or prefilled" (plain 503, 409, other action, after a finished repeat, outcome drop on reopen) pass at base trivially, because base kept nothing |
| 6 | Removal form | After `id_reused` and a re-read showing the line gone the form closes and the sentence stays on the step; "removed, but the re-read failed" locks the removal note (Remove again is the same request) | `noteBoxesDecisions` (id_reused, gone) and `noteReviewRound1Decisions` (removal not read back locks); must-not-fire: id_reused with the line still listed keeps the form; a plain 409 does not lock | Gone and not-read-back tests fail; the still-listed control passes |
| 7 | `adjustmentFormDirty` ignored the note | Counts `adjNote.trim() !== ''` | `noteReviewRound1Decisions`: unit test and the UI (Next held for a note alone, free for blanks) | Both fail |
| 8 | A refused note showed two messages | The wizard returns nothing for a refused note (`stayOpenOnFinish`), so only the field message shows | `noteBoxesDecisions` (both 400 codes) and `noteReviewRound1Decisions` (one alert, in the field; a 403 still shows the shell's bar) | The two 400-code tests and the one-message test fail; the 403 control passes |
| 9 | The "renders in the dark theme" tests proved nothing | Deleted both; replaced by a source scan: `NoteField.tsx` uses `tokens.color*` and no hex, `rgb()` or `hsl()` colour, with controls that the scan fires on a hex and an rgb() colour | `noteBoxesPage` "takes its colours from Fluent tokens only" (the whole-source colour guard in `InvoiceReviewPage.test.tsx` still covers every file) | Not a behaviour change: the old tests passed at base by construction; the new scan passes at base too (NoteField already used tokens) |

The 409 codes of item 2, by the server's names. **Kept open** (resending can succeed once the cause passes, or nothing changed): `ebilling.invoice.busy`, `ebilling.line.pending`, `ebilling.line.corrections_pending`, `ebilling.invoice.totals_refused`, `ebilling.correction.firm_missing`, `ebilling.correction.code_set_unavailable`, `ebilling.correction.extraction_unavailable`, and any `*.concurrent_change`. `ebilling.line_decision.firm_missing` was already kept open. **Closed with Reload, text kept for the next opening**: `ebilling.invoice.not_in_review`, `ebilling.line.not_decidable`, `ebilling.line.changed`, `ebilling.invoice.changed`, `ebilling.correction.line_not_correctable`, `ebilling.correction.no_provenance`, `ebilling.correction.procedure_gap`, `ebilling.correction.too_large`, and any code not listed (the safe default: the page may be out of date).

Choices worth a second look: an unsure send is `isOutcomeUnknown` = a 503 that names a step or lines (or a documented incomplete code) or status 0; a plain 503 that saved nothing does not lock (the add adjustment form, which already locked on any 503, still does). Locks are plain memory in the provider (not React state): the forms read them once on opening.

Counts after round 1: 388 passed, 388 total (13 suites), up from 339 (+51 new tests, -2 dark-theme tests). Typecheck: 0 surface errors (the 99 shared-lib errors are ignored by the gate as before). `npm run build` passes.

## Known limits

- The blocked view's `GET /blocked` returns the reason only, so the note is not shown there.
- A dialog whose send was answered 503 incomplete closes and is finished with the page's Repeat (the exact request from the closure); the text is also kept as a draft for the next opening, but the page notice's Repeat is the way to finish it.
- Reject/return: the server drops an edited note on a repeat after the record exists (K-1); mitigated by locking the reason and the note after an unsure send, and keeping that lock when the wizard is reopened.
- Control characters are checked only by the server; the client counts the limit only.
- Existing Reason textareas still carry `maxLength`.
- ESLint is not installed and has no config in this solution, so the `lint` script cannot run (pre-existing).
