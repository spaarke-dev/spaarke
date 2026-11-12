# Task 186 — Send Email dialog: templates, AI drafting, attachments (D-219 Q3)

PR [#2744](https://github.com/spaarke-dev/spaarke/pull/2744) squash-merged to master as `76b2930961` (owner: merge on green) (branch `fix/send-email-dialog-templates-ai`, worktree `C:/wt186`).

## Deviations from the POML
- **Three handlers, not two.** `EmailComposer` shows the template picker only with BOTH `onListEmailTemplates` and `onRenderEmailTemplate` (`EmailComposer.tsx:776`), so `onRenderEmailTemplate` is passed too.
- **Attachment upload added.** Without `onUploadLocalAttachment` a locally picked file is display-only (`createXrmEmailComposeHandlers.ts:255-262`). The owner asked for attachments on (D-219 Q3), so both hosts pass it (F4, required functionality).
- **Acceptance criterion 1 (every host) is met for the two hosts behind the owner's report only.** The remaining hosts are task 188, or owner questions (below).

## Host inventory (non-test SendEmailDialog / SendEmailPane / SendEmailPage / direct EmailComposer hosts)

| Host | Had handlers | This task |
|---|---|---|
| `SpaarkeAi/.../conversation/ConversationPane.tsx:3588` (Quick Start "Send Email", "Send as email") | no | **wired** |
| `LegalWorkspace/.../FilePreview/FilePreviewDialog.tsx:169` | no | **wired** |
| `SpaarkeAi/.../EmailPerItemCards.tsx` (useEmailComposeActions) | yes | — |
| `Spaarke.AI.Widgets/.../EmailWorkspaceWidget.tsx` | yes | — |
| `EmailPage/src/main.tsx` | yes | — |
| `LegalWorkspace/src/sections/email.registration.ts` | yes | — |
| `DocumentUploadWizard/.../DocumentEmailStep.tsx` | yes | — |
| `Spaarke.Compose.Components/.../ComposeWorkspace.tsx:5390` (memo email) | no | task 188 |
| `pcf/TrackingFieldTrio/index.ts:1794` (Email Members; has `getEmailHandlers()`) | no | task 188 |
| `code-pages/CommunicationPage/.../EmailComposerSlot.tsx:99` | no | task 188 |
| `pcf/CommunicationActions/.../CommunicationActionsApp.tsx:576` | no | task 188 |
| `Spaarke.DailyBriefing.Components/.../DailyBriefingApp.tsx:873` | no | **owner question**: passes `bffBaseUrl=""`, so the factory cannot build render/AI/upload |
| `office-addins/.../EmailView.tsx:307` (SendEmailPane) | no | **owner question**: Outlook add-in has no Xrm; template listing uses `Xrm.WebApi` |

## Tests
- `ConversationPane.send-email-dialog-handlers.test.tsx` 2/2 (fails on the old file); SpaarkeAi conversation folder 78 suites / 745 tests.
- `FilePreviewDialog.emailHandlers.test.tsx` passes under a throwaway jest config; LegalWorkspace has **no jest runner**, so CI does not run it (K-class known limit, same as its sibling tests).
- Composer gating already covered: `EmailComposer/__tests__/templatePicker.test.tsx`, `aiSparkle.test.tsx`.
- client-lint `spaarkeai`, `legal-workspace-app` pass. SpaarkeAi/LegalWorkspace `tsc` show only pre-existing errors, none in touched files.

## Review (Step 9.5)
Main-session review of the diff: one memoized handler instance per host; no change to `SendEmailDialog.tsx` (uac-r2 #2739 touches it). No F-class finding open.

Owner D-220: Daily Briefing stays without these; the Office add-in gets them after coordination (task 189).

rework: none task 186
