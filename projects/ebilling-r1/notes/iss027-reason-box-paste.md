# ISS-027 (#2878): Reason boxes no longer cut a paste at 2,000 characters

> **Task:** `tasks/iss027-reason-box-paste.poml` (FULL) · **Reference:** `084h-4-record-procedures-note-ui.md` ("Review round 1", the Note box) · **Base:** `task/ebilling-iss027` from f5ec8b6a20 · **Not deployed.**

## What was wrong

The Reason textareas of the Procedure, Rule and Assign Procedure wizards set `maxLength={MAX_REASON_CHARS}` (2000). A paste over 2,000 characters was cut by the browser with no message, and the cut text was saved as the Procedure change's reason.

## What changed (client only, `src/solutions/RecordProcedures/src`)

| Item | Where |
|---|---|
| `reasonProblem(reason)`: "The reason may be at most 2000 characters." when the trimmed text is over `MAX_REASON_CHARS`, else null | `components/wizards/plan.ts`, next to `reasonValid` |
| `ReasonField`: one Fluent `Field` + `Textarea`, no `maxLength`, hint `N / 2,000 characters.` (the Note box's `noteCount`, trimmed length), `validationState="error"` and the message when over | `components/wizards/steps.tsx` |
| The three wizards render `<ReasonField>` instead of their own three copies; unused `Textarea` / `MAX_REASON_CHARS` imports dropped | `AssignProcedureWizard.tsx`, `ProcedureWizard.tsx`, `RuleWizard.tsx` |

No change to the limit (still `MAX_REASON_CHARS` = `MAX_NOTE_CHARS` = 2000, pinned to the server by `serverWordsPin.test.ts`) and none to what is sent within the limit.

**How the hold works.** Unchanged: `reasonValid` is false for a reason over the limit, so `procedureInputValid` / `planWithin` / `planAssign` give no steps and Next is off on the choices step. Finish (on Confirm) is therefore unreachable, and `onFinish` sends nothing for an empty plan. What was missing was the word: the user now sees the count and the message. One deliberate difference from the Note box: the Note box is on the Confirm step, so it holds Finish; the Reason box is on the choices step, so it holds Next, which is what holds Finish.

## Tests

`src/__tests__/ReasonPaste.test.tsx` (new). `WizardsFix1.test.tsx` had a test pinning `maxlength=2000` on the Reason boxes; it now pins the absence (the old assertion is the flaw).

| Acceptance criterion | Test |
|---|---|
| Procedure wizard: 2,600 kept whole, count and message shown, Next off, nothing sent | `Procedure wizard: the Reason box > starts empty at 0 / 2,000 ...` (also Pin: `Pin shares the box ...`) |
| Rule wizard: same | `Rule wizard: the Reason box > a 2,600-character paste ...` |
| Assign wizard: same | `Assign Procedure wizard: the Reason box > Pin: a 2,600-character paste ...` |
| Within the limit sent unchanged | Each of the above, second half: trimmed to 2,000, Next on, Finish sends exactly 2,000 characters (one write) |
| Message rule | `reasonProblem` cases (undefined, blank, at limit, at limit with outer spaces, one over, 2,600) |

Results and fail-before evidence are in the section below.

## Results

- `npx jest` (RecordProcedures): 23 suites, 488 tests pass (478 before; 10 new in `ReasonPaste.test.tsx`).
- `npm run build`: succeeds (typecheck gate: surface-owned 0 errors, 99 pre-existing shared-lib errors deferred as before; 3,528 modules; `dist/` not committed, nothing deployed).
- `npm install --legacy-peer-deps --no-audit --no-fund` rewrote `package-lock.json`; reverted, not in the commit.

**Fail-before.** With the three wizard files restored to base (`maxLength` back) and the new helper, field and tests kept: 5 tests fail (Procedure Exclude, Procedure Pin, Rule Exclude, Assign Pin in `ReasonPaste.test.tsx`, and the updated `WizardsFix1` no-maxlength assertion); the 6 `reasonProblem` cases and the rest pass (14 of 19 in the two files). The wizard files were restored afterwards.

## Out of scope, noticed

- `npm run lint` for RecordProcedures cannot run (no flat ESLint config); pre-existing, already reported in 084h-4's notes.
- The Rule wizard's Change path uses the same `ReasonField` (one shared field); it has no separate paste test, the Exclude test covers the shared box.
