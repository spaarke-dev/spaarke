# UAT Feedback R5 — 2026-07-20 (Ralph, dev, after the R4 round-2 close-out deploy)

## Backlog + status

| Item | Feedback | Status |
|---|---|---|
| **R5-1** | "Revise in Compose" (top of files row) should be a **card in line** with the other cards (Summarize / Create matter / …) | ✅ shipped `45c29213a` — now a `local:revise-in-compose` action card appended to the post-attach chip set; tray button removed |
| **R5-2** | Add whitespace between the action-confirmation (File attached / classified / Summarized text) and the follow-on cards | ✅ shipped — `ConsumerChips` strip `paddingTop` → `spacingVerticalL` |
| **R5-3** | Remove the `?` icon — not accurate/useful | ✅ shipped — floating `HelpAffordance` removed (`/help` still opens the panel) |
| **R5-4** | Composer attachment pill: put files in the paperclip row / move paperclip above send; smaller `x` in a pill | ⏳ NEXT (shared-lib) — this is SprkChat composer layout in `@spaarke/ui-components` (Region 1 chip strip + Region 2 controls). Cross-consumer blast radius; needs its own rebuild + regression of other SprkChat surfaces |
| **R5-5** | History: smaller/no-bold text; clicking an entry should LOAD that session — nothing happens | ✅ shipped — `itemTitle` → base200 + regular weight; click now `setChatSessionId(id)` + remount so SprkChat `resumeSession`→`loadHistory` re-run (the real defect: the click only set state nothing re-read) |
| **R5-6** | "Should the Context pane be logging activities — unclear what goes there?" | ℹ️ answered in chat — it's the **Execution Trace** (tool calls from the session ledger). Ties to deferred R4-9 (consistency). No code change this round |
| **R5-7** | Create Event wizard did not load the file | ⏳ NEXT (shared-lib + code-page) — the launch envelope ALREADY carries the files (via `create-task` surface_launch); `CreateEventWizard` just doesn't consume `initialFileRefs`. Add the prop (mirror CreateMatterWizard) + wire `CreateEventWizard/main.tsx`. Smallest of the wizard fixes |
| **R5-8** | Quick Start: Assign Work / Summarize / Find Similar did not load the file | ⏳ NEXT (shared-lib + code-page, larger) — none has a registry entry + a file-consumption seam. Assign Work needs registry + `initialFileRefs` (like matter). Summarize + Find Similar are upload-driven with a **different id type** (Dataverse doc id vs session file id) — each needs a NEW session-bytes ingestion seam (fetch `GET .../sessions/{id}/documents/{fileId}/content` → drive the existing upload path) |
| **R5-9** | Send Email should open the Email Compose modal (new shared component) | ✅ shipped — post-Draft "Send as email" chip + Quick Start "Send Email" card now open the shared `SendEmailDialog`/`EmailComposer`, seeded from the drafted correspondence (subject/body/recipients). Client-only |

## R5 round-1 (2026-07-20) — client-only batch

Shipped `45c29213a` (merged origin/master `3a74acc40`), deployed `sprk_spaarkeai` to dev. **No BFF, no catalog** change.
New client seams: `localActionChips.reviseInCompose`; `useConsumerChips` gained `getAppendedLocalChips` (append local chips to a delivered set) + `onCorrespondenceDraft` (stash draft for the email seed); `QuickStartModal.onSendEmail`; `ConversationPane` mounts `SendEmailDialog`. Tests: 127 pass.

## Still open (next chunk — shared-lib + wizard code-pages, multi-deploy)
- **R5-4** SprkChat composer layout (blast radius: every SprkChat consumer)
- **R5-7** Create Event file leg (component `initialFileRefs` + code-page)
- **R5-8** Assign Work / Summarize / Find Similar file legs (registry + new ingestion seams)
- **R4-7 / R4-9** still awaiting owner repro
