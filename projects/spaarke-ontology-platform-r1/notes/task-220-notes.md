# Task 220 — email composer: discard prompt, session upload cleanup, network wording (#2813 DI-154, #2946 DI-158)

Branch `work/ontology-220-composer-discard-cleanup` from `origin/master` 2ac250cae4 (worktree `C:\wt220`). PR to master.
Rigor FULL, opus @ high, directional steps.

## Step 1 — findings (origin/master 2ac250cae4)

**Hosts.** Every host that passes `onUploadLocalAttachment` gets it from the one factory
`createXrmEmailComposeHandlers` (`EmailComposer/createXrmEmailComposeHandlers.ts:260`) and forwards the function
reference unchanged: CommunicationActions PCF (`SendEmailPage`), TrackingFieldTrio PCF (`SendEmailDialog`),
`useEmailComposeActions` / EmailWorkspace (`SendEmailDialog`), ComposeWorkspace (`SendEmailDialog` + an inline
`EmailComposer`), LegalWorkspace `FilePreviewDialog`, SpaarkeAi `ConversationPane` + `EmailPerItemCards`
(`SendEmailDialog`), DocumentUploadWizard `DocumentEmailStep` (inline `EmailComposer`), and the create wizards'
`SendEmailFollowOnStep` (inline, compose-only; Finish sends through `sendComposedEmail`).

**Upload.** `EmailComposer.tsx:721` `handleAddAttachment` uploads a local pick at once and patches the item with its
`documentId`. Not cleaned up today: removal (`REMOVE_ATTACHMENT`), Cancel/×, a failed send then Cancel, and an upload
that resolves after the composer unmounted (`if (!mountedRef.current) return;`: the document is created and nobody ever
sees its id).

**Existing delete paths (no new BFF route).**
- `Xrm.WebApi.deleteRecord('sprk_document', id)`: the factory already uses it (`createXrmEmailComposeHandlers.ts:318`)
  to remove the document it just created when the attach step fails; also `EntityCreationService.ts:841`,
  `DocumentRecordService.ts:103`.
- BFF `DELETE /api/documents/{id}` (`DataverseDocumentsEndpoints.cs:343`; `useDocumentActions`, SpeDocumentViewer).
Neither deletes the SPE file ("This route deletes no file"): the row goes, the uploaded file stays (same as the
factory's existing compensating cleanup).

**Why the client must decide "not referenced".** `sprk_communicationattachment_Document_1n` is `CascadeDelete=RemoveLink`
(`SpaarkeMaster/Other/Relationships/sprk_Document.xml:178-193`): deleting a document a SENT email's attachment row names
would silently unlink it, not be refused. So the composer must never delete a document a send attempt carried unless that
attempt provably sent nothing.

**Which failed sends provably sent nothing.** BFF `CommunicationService.SendAsync`/`SendAsUserAsync`: every 4xx
`SdapProblemException` is thrown before the Graph send (`:2580` / `:2947`); any unexpected exception after it becomes 500
`GRAPH_SEND_FAILED` (`:2829`, `:3185`). Client side: `CLIENT_ERROR` is a failure before the request left. So:
- not sent: status 0 `CLIENT_ERROR`, any 4xx, a link refused inline before the request;
- may have been sent: `NETWORK` (a dropped connection after the send looks the same), `SENT_UNCONFIRMED` (2xx without an
  id), any 5xx.

**Network wording.** `describeSendFailure.ts:100` classifies NETWORK only for `err instanceof TypeError` whose message is
one of four browser wordings. Anything else from a fetch with no response (a TypeError from another realm, WebKit's
"The Internet connection appears to be offline." / "The network connection was lost.", a host fetch that throws its own
error) falls to `CLIENT_ERROR` = "Something went wrong on this page and the email was not sent." (the dev check, #2946).
`sendCommunication` rethrows a transport failure unchanged (pinned by `communicationApi.test.ts` "rethrows a transport
failure unchanged"; the Office add-ins and CommunicationMessageActions call it too), so the classification is fixed
where the composer calls it, not inside it.

**Discard pattern.** `RuleWizard.tsx:591` `handleClose` compares the draft with its initial state and opens
`ConfirmModal` "Discard this rule?" (Discard / Keep editing, destructive).

**Conflict check (2026-11-25).** Soft warn only: #3133 (field-mapping-server-write-path-r1) edits
`createXrmEmailComposeHandlers.ts` (hunks at 272 and 305, mine is the return at 325), `composeOnlySend.ts` (I do not
edit it) and `SendEmailFollowOnStep.tsx` (I do not edit it); #1293 edits two EmailComposer test files I do not touch.

## Decisions

1. **Delete path: the upload hands back its own `discard` (opt-in, on the upload result).** `onUploadLocalAttachment`
   now returns `IUploadedLocalAttachment { documentId, driveItemId?, linkUrl?, discard? }`. The one factory
   (`createXrmEmailComposeHandlers`) returns `discard = () => Xrm.WebApi.deleteRecord('sprk_document', documentId)`,
   the delete it already runs on the same row when its attach step fails. No new BFF route, no schema change.
   Why on the result and not a separate `onDeleteDocument(id)` prop (the POML says "opt-in prop"): the delete is bound
   to the exact row that upload call created, so the composer cannot even express "delete a document I did not
   upload"; a picked or carried document has no delete. Every host already forwards the factory's function reference
   unchanged, so all of them get the cleanup with no host file edits; a host whose uploader returns no `discard`
   (tests, any future non-Xrm host) keeps today's behaviour. §11: Existing — the factory's own compensating delete;
   Extension — this extends the upload contract instead of adding a prop threaded through ~10 hosts; Cost of doing
   nothing — every cancelled or re-attached upload leaves an unreferenced `sprk_document` (#2813).
   Why the Xrm delete and not BFF `DELETE /api/documents/{id}`: both delete the row and not the file, both are refused
   by Dataverse Restrict relationships (child documents, Stored File copies); the Xrm one is what this handler already
   uses, needs no extra wiring, and runs as the user who created the row.
2. **When the composer deletes** (`EmailComposer.tsx`): only documents in its own session-upload map, and only
   - the file is removed from the draft (remove-then-reattach leaves nothing behind);
   - the draft is abandoned (Cancel, or the dialog's ×, after "Discard this email?" when there is typed work);
   - an upload finishes after the draft was abandoned, after the file was removed, or after the composer unmounted:
     its id never reached the draft, the host or a send (a wizard step that unmounts mid-upload re-uploads the file
     itself — `SendEmailFollowOnStep` "stranded uploads").
   Never: after a successful send (closing then neither asks nor deletes); for a document a send attempt carried while
   its outcome is unknown or may have delivered (no response, 5xx, 2xx without id — `mayHaveBeenSent`); for a document
   a Save Draft carried (the saved draft may name it; no host wires `onSaveDraftRequest` today, so this is a guard for
   when one does); for a picked/carried document. "Abandoned" is not a persistent flag: only the uploads in flight at
   that moment are marked, so a host that keeps the composer mounted after Cancel does not lose later uploads. A refused send
   (4xx, client-side) releases its documents again. **Failed send: the failure itself deletes nothing** (the dialog
   stays open with the draft and its uploads for a retry); cleanup happens when the user then abandons the draft.
   Deletes are best effort: a rejected delete is logged and the document stays (as before).
3. **Discard prompt** reuses `ConfirmModal` exactly as RuleWizard does ("Discard this email?", Discard / Keep editing,
   destructive). "Typed work" compares To/Cc/Bcc, subject, body words (tags and spacing ignored, so editor
   re-serialization never counts) and attachments with what the composer opened with, so an untouched reply or a
   host-seeded draft closes without asking. Read-only (view) never asks. `SendEmailDialog` routes the shell × through
   the new `IEmailComposerHandle.requestCancel()`, the same path as the composer's Cancel. Inline mounts (wizard
   steps, the Outlook/Word pane) have no Cancel and are unchanged.
4. **Network wording.** The composer classifies what the `sendCommunication()` call itself threw
   (`toSendRequestError`): anything that is not one of its own errors or an HTTP outcome came from a fetch with no
   response → `NETWORK`, whatever the browser called it or whichever frame threw it. `toSendCommunicationError` also
   matches a `TypeError` by name (cross-frame) and WebKit's offline / connection-lost sentences, which also fixes the
   wizards' Finish warning (`composeOnlySend`, not edited — #3133 rewrites that hunk). `sendCommunication` still
   rethrows transport failures unchanged (its pinned contract; the Office add-ins and CommunicationMessageActions call
   it). The message now leads with "The connection to the Spaarke server failed before it answered." **It does not
   say "the email was not sent"** (the issue's literal wording): a connection that drops after the send looks the
   same, and the existing rule is never to claim a definite non-send (a resend would duplicate it); the title stays
   "Email may not have been sent", with the Sent Items check and "Your draft is still here". HTTP 500 wording is
   unchanged.

## Test evidence (2026-11-25)

- **Fail before.** `composerDiscardAndUploadCleanup.test.tsx` on the origin/master sources: 16 of the first 20 fail (the 4
  that pass are the controls: empty draft, pre-filled draft, successful send, HTTP 500 wording). The two review-round tests
  (Save Draft then discard; close after a successful send) fail on the pre-review composer. The factory `discard` tests and
  the new `describeSendFailure` network cases fail on master.
- **Pass after.** New composer suite 22/22; factory discard 2/2; EmailComposer folder 36 suites / 403 tests; full
  Spaarke.UI.Components 488 suites / 7495 tests (before the review round); `tsc --noEmit` 0 errors.
- **Hosts.** Spaarke.Communication.Components 43 suites / 452 tests; Spaarke.AI.Widgets CreateAnalysisWizardWidget 12/12;
  SpaarkeAi ConversationPane send-email dialog + filePreviewDialog email handlers 6/6; Office add-ins EmailView 16/16.
  Not runnable here: Spaarke.Compose.Components memoEmailHandlers and two DailyBriefing suites cannot resolve
  `@spaarke/ui-components` (its dist is not built in this worktree; the Compose test mocks that module entirely).
- **client-lint** `check`: all 14 packages ok.

## Self-review (code-review, no sub-agents) — fixed in scope

1. Save Draft then discard would have deleted documents a saved draft names (no host wires `onSaveDraftRequest` today):
   a draft save now keeps every document it carried.
2. Close after a successful send asked "Discard this email? ... will not be sent": now it just closes, deleting nothing.
3. "Abandoned" was a persistent flag: a host that keeps the composer mounted after Cancel would have had every later
   upload deleted on arrival. Now only uploads in flight at abandon are marked.

## Fix round 1 (coordinator, 2026-11-25): the create wizards' Send Email step

The wizards were the residual: the step's composer unmounts on Next, so its own upload map was lost, and the wizard
closed without telling anyone. Conflict check first: no open PR other than #3145 touches `composeOnlySend.ts`,
`SendEmailFollowOnStep.tsx` or the wizard hosts (#1293 touches two unrelated EmailComposer tests).

- **Provenance travels with the item.** `IAttachmentItem.discard` is set from the upload result
  (`RESOLVE_ATTACHMENT_DOCUMENT`), lifted into the wizard's `IComposedEmail`, and restored when the step reopens. The
  engine's own session map is replaced by it (same rule: only an item that carries a delete is ever deleted; picked and
  host-passed items never carry one).
- **One gate, `EmailComposer/uploadDiscard.ts`.** Keyed on the `discard` function: `keepUploads` / `releaseUploads`
  (a send that carried it and went out or may have keeps it for good) and once-only `discardUpload`. §11: Existing —
  none (the engine's per-instance set does not survive the step unmounting); Extension — `composeOnlySend.ts` imports
  `EmailComposer.tsx`, so the engine cannot import a gate from there without a cycle; Cost of doing nothing — a Finish
  send's documents would be deleted when the wizard closes, or every closed wizard leaves its uploads behind.
- **`sendComposedEmail`** keeps what it carries before the request and releases it only when nothing was sent (a
  refused link, a 4xx, a failure before the request). It classifies what the request threw with `toSendRequestError`,
  so a host fetch's own error is a possible send (kept), not "on this page".
- **Close, not reopen.** `useDiscardComposedEmailUploadsOnClose(open, emailRef)` runs when `open` turns false or the
  wizard unmounts while open. Wired into every host that holds the step's email: CreateRecordWizard (Matter, Project,
  Event, To Do, and the Analysis / Report Card shells), CreateReportCardWizard, WorkAssignmentWizardDialog,
  SummarizeFilesDialog, DocumentEmailWizard, CreateAnalysisWizardWidget. The hook lives in `uploadDiscard.ts`, not the
  `WizardFollowOns` barrel, because host tests mock that barrel.
- **Step re-upload.** A re-uploaded stranded file carries its `discard`; a re-upload result the step drops (closed, or
  the file removed meanwhile) is deleted instead of left behind (it was before).
- **Every Finish sends the same items.** Matter (`followOnActions.sendEmail.email`), Project, Event, To Do
  (`context.followOn.email`), Work Assignment (`service.sendEmail(..., email)`), Summarize, Document Email, Analysis and
  Report Card (`emailRef.current`) all pass the composed email itself, so a sent document is always kept.
