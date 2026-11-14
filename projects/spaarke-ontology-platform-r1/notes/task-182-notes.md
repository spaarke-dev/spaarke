# Task 182 notes: Send Email follow-on step is the shared EmailComposer (D-219, F-R4-3)

Branch `task/ontology-182-send-email-followon-composer` (from origin/master 3ae2a63362). Rigor FULL. Model opus/high per
POML, run as implementer. Step mode directional.

## What changed
- `EmailComposer`: `composeOnlyShowsFrom` (default off) shows the From switcher in compose-only; Send stays hidden.
- `EmailComposer/composeOnlyText.ts`: `toPlainText` (moved from DecisionWizard `EmailMessageFields`), plus `plainTextToHtml`.
- `EmailComposer/composeOnlySend.ts` (new; not in the POML file list): `IComposedEmail`, `isComposedEmailReady`, and
  `sendComposedEmail`, which runs the composer's own request path (share-link resolution, `mapStateToSendRequest`,
  `sendCommunication`) at Finish and never throws. Option `archiveToSpe` (Summarize Files passes false, as before).
- `SendEmailFollowOnStep`: the composer, compose-only, with From, To, Cc, Bcc, attachments, templates and AI draft. It
  opens in rich text (where the RTF toolbar holds template/AI/attach). An unedited plain-text pre-fill stays plain text:
  compared by visible text (spacing collapsed) and only when the HTML has no formatting. Back/Next restores everything; a
  host change restarts the composer; an upload that was still running when the step closed is uploaded again.
- Wizards sending at Finish via `sendComposedEmail`: Matter (matterService), Project, Event, To Do, Report Card, Work
  Assignment (workAssignmentService), Summarize Files (no association), and the Console Create Analysis widget
  (`CreateAnalysisWizardWidget`, found in review: it still used the old props and would have crashed).
- Hosts inject `createXrmEmailComposeHandlers`: InAppWizardHost, useWizardPageBootstrap, 5 code-page main.tsx, SmartTodo,
  CreateMatterWizardWidget (pass-through), Console WorkspacePane (both Create Analysis mounts).
- Pre-fill runs once per opening in CreateRecordWizard and Summarize Files (clearing the subject used to reset subject
  and body; pre-existing defect found in review).

## Deviations
- `EntityCreationService.ts` unchanged (its `sendEmail` is still used elsewhere). LegalWorkspace's duplicate matterService
  not changed (not a host of the shared step). Invoice wizard has no Send Email step.
- Work Assignment's free-text CC box is gone: Cc is the composer's Cc row (auto-Cc of the current user still added).

## Known limits
- Restored recipients keep the address only (display names are lost after Back/Next).
- An upload re-run after the step closed leaves the first upload's `sprk_document` unreferenced.
- Template merge fields have no regarding record while composing (the record does not exist yet).
- Project / Event / Report Card skip the email silently when the host gives no authenticated fetch or BFF URL
  (pre-existing behaviour).
- `CreateMatterWizardWidget` forwards `emailComposeHandlers`, but nothing in the code puts them in its widget data: the
  widget is unreachable with handlers today (no template / AI / upload there).
- Orphaned uploads: a file uploaded from the step leaves an unreferenced `sprk_document` when the wizard is cancelled,
  the send fails, or the step re-runs an upload after Back. The coordinator is filing an issue.

## Fix round 2
- F1-a: every host rewrite of the body also sets `bodyFormat: 'PlainText'` (Summarize toggle + pre-fill,
  CreateRecordWizard pre-fill, Work Assignment pre-fill + refresh).
- F1-b: Finish warnings normalised via `toSendCommunicationError` + `meaningfulDetail`; an unconfirmed 2xx says the email
  may have been sent (check Sent Items / the record's Messages first); 401 says sign in and send from the record; no
  "draft" wording, no "HTTP 500".
- F2: Project, Event, Report Card and Create Analysis push `EMAIL_NOT_SENT_NO_SERVER` instead of skipping silently;
  Summarize's developer string replaced. Matter, Work Assignment, To Do and Summarize always call `sendComposedEmail`,
  which turns a missing fetch into a visible warning.
- Finish tests added: Matter (`matterService.sendEmail.test.ts`), Work Assignment (dialog + service), Summarize Files
  (toggle + Finish). Skipped: Event, To Do, Report Card Finish-send tests and the no-BFF warning tests for Event, Report
  Card and Create Analysis — each needs its own full wizard harness; the same code shape is covered by the Project
  no-BFF test and the shared `sendComposedEmail` tests.
- Host code pages could not be type-checked standalone (shared packages resolve through Vite aliases); the
  CreateMatterWizard page builds with Vite. AI.Widgets tsc needs its dist builds; its jest suites run.
