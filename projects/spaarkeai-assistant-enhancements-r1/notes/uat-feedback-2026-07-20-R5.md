# UAT Feedback R5 — 2026-07-20 (Ralph, dev, after the R4 round-2 close-out deploy)

## Backlog + status

| Item | Feedback | Status |
|---|---|---|
| **R5-1** | "Revise in Compose" (top of files row) should be a **card in line** with the other cards (Summarize / Create matter / …) | ✅ shipped `45c29213a` — now a `local:revise-in-compose` action card appended to the post-attach chip set; tray button removed |
| **R5-2** | Add whitespace between the action-confirmation (File attached / classified / Summarized text) and the follow-on cards | ✅ shipped — `ConsumerChips` strip `paddingTop` → `spacingVerticalL` |
| **R5-3** | Remove the `?` icon — not accurate/useful | ✅ shipped — floating `HelpAffordance` removed (`/help` still opens the panel) |
| **R5-4** | Composer attachment pill: put files in the paperclip row / move paperclip above send; smaller `x` in a pill | ✅ shipped `7b9d249cf` — pills merged into the controls row with Prompt/Attach (one wrapping row); compact per-pill `×`. Shared `SprkChat` change (414 tests pass, test IDs preserved). Other SprkChat surfaces adopt it on next rebuild |
| **R5-5** | History: smaller/no-bold text; clicking an entry should LOAD that session — nothing happens | ✅ shipped — `itemTitle` → base200 + regular weight; click now `setChatSessionId(id)` + remount so SprkChat `resumeSession`→`loadHistory` re-run (the real defect: the click only set state nothing re-read) |
| **R5-6** | "Should the Context pane be logging activities — unclear what goes there?" | ℹ️ answered in chat — it's the **Execution Trace** (tool calls from the session ledger). Ties to deferred R4-9 (consistency). No code change this round |
| **R5-7** | Create Event wizard did not load the file | ✅ shipped `e20d1cc7a` — extracted the Matter file-leg into a shared `useHandoffFileLeg` hook (§11); `CreateEventWizard` consumes `initialFileRefs` → seeds its Upload step. Deployed `sprk_createeventwizard` |
| **R5-8** | Quick Start: Assign Work / Summarize / Find Similar did not load the file | ✅ shipped `e498fc724` (Assign Work) + `0a7d838bd` (Summarize + Find Similar) — registry entries `create-work-assignment` / `summarize-files` / `find-similar`; each wizard fetches the session bytes and pre-seeds its file input (Find Similar = first file, single-doc). QuickStartModal routes all three through `launchSurface`. Deployed all three code pages + `sprk_spaarkeai` |
| **R5-9** | Send Email should open the Email Compose modal (new shared component) | ✅ shipped — post-Draft "Send as email" chip + Quick Start "Send Email" card now open the shared `SendEmailDialog`/`EmailComposer`, seeded from the drafted correspondence (subject/body/recipients). Client-only |

## R5 round-1 (2026-07-20) — client-only batch

Shipped `45c29213a` (merged origin/master `3a74acc40`), deployed `sprk_spaarkeai` to dev. **No BFF, no catalog** change.
New client seams: `localActionChips.reviseInCompose`; `useConsumerChips` gained `getAppendedLocalChips` (append local chips to a delivered set) + `onCorrespondenceDraft` (stash draft for the email seed); `QuickStartModal.onSendEmail`; `ConversationPane` mounts `SendEmailDialog`. Tests: 127 pass.

## R5 round-2 (shared-lib + wizard code-pages) — SHIPPED
All of R5-4/7/8 shipped + deployed. New shared seam: `useHandoffFileLeg` (CreateRecordWizard) — one
file-leg impl reused by Matter/Event/Assign-Work/Summarize; Find Similar uses a code-page-local
variant (single-doc). Registry grew to 7 entries. Code pages deployed: `sprk_createeventwizard`,
`sprk_createworkassignmentwizard`, `sprk_summarizefileswizard`, `sprk_findsimilar`, `sprk_spaarkeai`.
Commits: `e20d1cc7a` (Event), `e498fc724` (Assign Work), `0a7d838bd` (Summarize + Find Similar),
`7b9d249cf` (R5-4 composer).

## Still open
- **R4-7 / R4-9** awaiting owner repro (empty "Actions available" header + Context consistency).
- Other **SprkChat consumers** (ribbon side-pane, other code pages) adopt the R5-4 composer layout
  only when they're next rebuilt/redeployed — no regression, just not yet applied there.
