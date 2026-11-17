# 084h-4: RecordProcedures Note box and the NotRecorded warning

> **Task:** `tasks/084h-4-record-procedures-note-ui.poml` (STANDARD) · **Design:** `084h-procedure-change-decision-records-design.md` §7.1, §9, §17 (EB-D-238, EB-D-239) · **Server contract:** `084h-2-procedure-change-records-service.md`, `084h-3-repair-job.md` · **Base:** `task/ebilling-084h-4` from 251f9602ef · **Status:** built and tested. Not deployed (084h-5 / 112).

## What was built

Client only, in `src/solutions/RecordProcedures/src`. No BFF, no shared-library change.

| Item | Where |
|---|---|
| `note` on `AssignRequest`, `ExcludeRuleRequest`, `ChangeRuleRequest`; `removeOverride(core, recordId, overrideId, note?)`; `DecisionRecordStatus`, `decisionRecord?` on `OverrideCreated` and `Removed` (a rule change carries it once, inside `override`); `noteToSend`, `noteProblem`, `notRecorded`, `MAX_NOTE_CHARS` | `services/overridesApi.ts` |
| `executePlan(..., note?)` puts the same note on every call of the plan; `PlanRun.notRecorded` | `components/wizards/plan.ts` |
| `ConfirmStep` gets the "Note" field (Fluent `Field` + `Textarea`, `maxLength` = `MAX_REASON_CHARS` = 2000, optional, with its inline problem); `NOT_RECORDED_LINE` | `components/wizards/steps.tsx` |
| The note state, Finish held off for a note the server would refuse, the note sent through `executePlan`, the warning bar on the success screen, the error-text addition for a partial save | `components/wizards/WizardFrame.tsx` |

One place covers all seven acts: every wizard (Procedure, Rule, Assign) renders `ConfirmStep` through `WizardFrame`, so assign, pin, unpin, exclude, rule exclude, change and remove all get the box. There is no per-wizard code.

## Behaviour

- **Sending.** The note is trimmed. Blank or whitespace-only is **absent**: the POST bodies have no `note` key (never `null`, never `""`), and the DELETE has no body and no `Content-Type`. With a note, the DELETE sends `{ "note": "..." }` with `Content-Type: application/json` (EB-D-239 Q8).
- **Plans of two calls** (remove, then create: Pin to Follow and every "change an existing override" plan) send the same note on both calls; each call files its own record (EB-D-239 Q6).
- **Limits, said before the call.** The input stops at 2000 characters. The server counts after trimming, so a pasted 2000 characters with outer spaces is accepted. A control character other than `\n`, `\r`, `\t` (the server's `char.IsControl` set: U+0000-0008, 000B, 000C, 000E-001F, 007F-009F) or more than 2000 characters trimmed puts a message under the field and turns Finish off at Confirm. `onFinish` re-checks as a backstop and sends nothing.
- **Server 400s.** If the server still refuses (`assignment.note_too_long`, `assignment.note_invalid_characters`), the wizard stays open on Confirm, the typed note stays, and the error bar shows the server's detail followed by the code in brackets. Other 400s get no code added.
- **The warning.** After a plan runs, if any answer's `decisionRecord.status` is `NotRecorded`, the success screen shows a warning bar (`success-not-recorded`): "The change was saved, but its decision record could not be written. Tell your administrator." For a rule change the status is read from `override.decisionRecord`. If a two-call plan's second call is refused after the first said `NotRecorded`, the same sentence is added to the error text, since the first change did save.
- **No warning** for `Recorded`, for `Queued` (since 084h-3 the server queues a failed inline record and retries it, so a failed inline record normally answers `Queued`; `NotRecorded` is the case where the enqueue also failed), for any other status, for `decisionRecord: null`, or for an answer with no member (an older server).

The warning words differ from design §7.1 ("Its Decision Record was not written. Support has been notified.") on purpose: the POML prompt gives the text above, and "Support has been notified" would not be true for a failed enqueue.

## Tests

`src/__tests__/WizardNote.test.tsx` (28) and `src/__tests__/overridesApiNote.test.ts` (21), plus the 420 existing tests, all green: 21 suites, 469 tests.

| Acceptance criterion | Evidence |
|---|---|
| Each plan step sends `note` when typed and omits the key when blank | WizardNote: Procedure Exclude (typed; blank, spaces, line break + tab each absent), Pin, Rule Exclude (typed and blank), Rule Change, Assign (typed, blank); overridesApiNote: the three POSTs, trimmed / blank / absent |
| The DELETE sends a JSON body only when a note is given | WizardNote: remove with a note (body `{ note }`, JSON content type) and with blanks (body and headers undefined); unpin; overridesApiNote: `{ method: 'DELETE' }` exactly when blank |
| 2000 characters is the input limit | WizardNote: `maxlength` attribute is 2000 and `MAX_NOTE_CHARS === MAX_REASON_CHARS`; 2001 held; exactly 2000 (with outer spaces) sent; control character held, line break and tab allowed |
| Pin-to-Follow sends the same note on both calls | WizardNote (remove then assign, both carry it); overridesApiNote `executePlan` |
| `NotRecorded` shows the warning; `Queued` and `Recorded` do not; no member shows none | WizardNote: NotRecorded (exact words, inside the success body), Queued and Recorded (must-not-fire), missing member, null, unknown status; removal; rule change (nested `override`); two-call partial; overridesApiNote `notRecorded` and `executePlan.notRecorded` |
| 400 codes surfaced | WizardNote: both note codes shown with the code and the wizard kept open with the note; another 400 gets no code |
| Existing suites stay green; build succeeds | 420 existing tests pass unchanged; `npm run build` succeeds |

**Fail-before.** With the four source files restored to their base versions and the new tests kept, 41 of the 49 new tests fail (the 8 that pass are the blank-note cases, which are true before the change as well). A mutation of `notRecorded` to `status !== 'Recorded'` fails 8 tests (every must-not-fire for Queued, missing, null and unknown); the source was restored after each run.

## Build and checks

- `npm run typecheck` (the surface gate): surface-owned 0 errors; 99 pre-existing errors in shared libs, deferred as before.
- `npm run build`: succeeds (3510 modules, `dist/sprk_recordprocedures.html` 1,343.72 kB). The bundle holds the warning sentence and the note code prefix (grep count 1 each). `dist/` is ignored by git; nothing was deployed.
- `npm run lint`: **cannot run**, as before this task. The page has no `eslint.config.*` and `eslint` v10 will not start without one (`ESLint couldn't find an eslint.config.* file`). `scripts/quality/client-lint` does not list RecordProcedures. This is pre-existing and is reported, not fixed here.
- `npm install --legacy-peer-deps --no-audit --no-fund` rewrote `package-lock.json`; that change was reverted and is not in the commit.

## Known limits

- **K-1.** The dry run (Impact step) does not carry the note. The server's dry run writes nothing and records nothing, and does not validate a note (084h-2), so the client does not send it.
- **K-2.** A failed first call of a two-call plan never reaches the second, so no second record exists; the note is kept in the box for a retry.
- **K-3.** `Queued` is silent by design (POML). If the repair job later fails, nothing reaches the user; that is the job's own poison and dead-letter path (084h-3).
- **K-4.** Existing page behaviour, unchanged: a refused change keeps the wizard open, and the box keeps the note until the wizard moves to another row (the key reset clears it).

## Out of scope, noticed

- `npm run lint` for RecordProcedures is broken as above (no flat ESLint config). Not filed: no failure on a real path; the page is not in the client-lint surface list. Worth adding when that list is next reviewed.

## Rework

rework: none task 084h-4
