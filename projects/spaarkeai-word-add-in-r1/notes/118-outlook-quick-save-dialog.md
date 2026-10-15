# Task 118 — Outlook Quick Save dialog + unfiled save

UAT round 12, item O5 (`042-uat-round12-2026-10-08.md`): B2B guest in Outlook on the web — Quick Save showed no pop-up
and, for a suggestion the user cannot file to (a contact), only an info bar "Spaarke suggests …, but you can't file to it".

## What changed

| File | Change |
|---|---|
| `shared/commands/quickSaveDialog.ts` (moved from `word/commands/`) | Host-neutral dialog controller; new required dep `pagePath` (the host's notify page). |
| `shared/commands/notify.html` (moved from `word/commands/`) | The one dialog page; webpack emits it as `word/commands-notify.html` AND `outlook/commands-notify.html`. |
| `shared/commands/readSavedDocument.ts` (new) | `readSavedDocument` + `QuickSaveResponse`, moved out of `word/commands/index.ts` unchanged; Outlook reuses it to read the saved .eml's `sprk_document` id from the save job. |
| `word/commands/index.ts` | Imports the shared controller/reader, passes `pagePath: '/word/commands-notify.html'`. Behaviour unchanged. |
| `outlook/commands/index.ts` | `quickSave` rewritten around the dialog (opens first). Item info bars removed. Unfiled path. |
| `shared/taskpane/services/quickSaveHelpers.ts` | `buildEmailSaveRequest` accepts `target: null` (no `targetEntity`); `targetEntity` optional in the body type; idempotency key `email:{id}|unfiled` for a null target (filed keys unchanged). |
| `webpack.config.js` | Notify page copied for both hosts from `shared/commands/notify.html`. |
| Tests | `word/commands/__tests__/{quickSaveDialog,notify}.test.ts` re-pointed (kept at their paths: `ci-gated-suites.txt` lists them); `outlook/commands/__tests__/commands.test.ts` rewritten, `commands.emailContent.test.ts` updated, `quickSaveHelpers.test.ts` +1 unfiled case. |

## Decisions

- **Unfileable or absent suggestion -> save UNFILED.** Covers `canFile === false`, no prediction, the 404 "not saved yet"
  and a prediction that failed to load (that last one logs a `console.warn`). The old "no prediction -> open the pane"
  path went too: a first-time email always has no suggestion, so Quick Save would never have saved it. Dialog text:
  "Saved to Spaarke — not filed to a record. Open Spaarke to file it." Filed: "Saved to Spaarke and filed to {name}."
  Duplicate: "This email is already saved in Spaarke. Nothing new was saved." Skipped attachments are still named.
- **Info bars:** none remain. Every outcome (progress, success, error) is in the dialog; a bar after a dialog would be stale.
- **404:** `communicationSuggestionsService.fetchModel` already resolves it to `null` (no throw), so Quick Save logs
  nothing for it beyond the browser's own network line. Pinned by a test (no console.error / warn on `null`).
- Quick Save no longer opens the task pane.

## Live checks still to do (Outlook on the web + desktop, B2B guest and member)

1. Dialog appears on click with the spinner, flips to success in place; "Open in Spaarke" opens the .eml `sprk_document`.
2. A contact suggestion -> unfiled save + message; the saved record shows no regarding record.
3. A fresh email (404) saves unfiled with no console error from Spaarke code.
4. A fileable matter suggestion -> filed; the message names the matter.
5. `outlook/commands-notify.html` is served from the Static Web App (same origin as the commands page).
6. Closing the dialog mid-save: the save finishes, the ribbon spinner ends afterwards.
