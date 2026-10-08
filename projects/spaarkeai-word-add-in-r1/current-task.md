# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. the new "Multi-customer add-in (Model 1)" block). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-08 (context-handoff before /compact). UAT round 12 in progress: 116 merged + live; 117-119 done but UNCOMMITTED; 120 running in a background agent.

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **UAT round 12** (`notes/042-uat-round12-2026-10-08.md`) — B2B guest (`ralph@deweycheatham.onmicrosoft.com`) in Outlook on the web + owner in Word. Tasks 117-120. |
| **Status** | 116 merged (#1403 `7adfa0205`), add-in deployed. **117, 118, 119: done, UNCOMMITTED in this worktree** (agents finished; gates green per their reports). **120: background agent RUNNING** — BFF `POST /api/documents/resolve-email-identity` + Outlook "already saved" green box + web open fallback. Owner APPROVED the 120 server addition and a dev BFF deploy (2026-10-08). |
| **Next Action** | 1) WAIT for the task-120 agent's completion notification — do NOT commit or run gates before it finishes (it edits App.tsx/SaveFlow/server files in this tree). 2) Then: add new client suites to `src/client/office-addins/ci-gated-suites.txt` (from 117: `shared/taskpane/components/__tests__/SaveFlow.copyRecordLink.test.tsx`; from 119: `shared/taskpane/components/__tests__/FindResultsList.openAffordance.test.tsx`; plus 120's reported suites); run full gates (jest, lint, tsc prod 0 / test debt ≤ 68, prettier; server: `dotnet build` + office-scope filter); TASK-INDEX rows 117-120 + POML statuses + drift check; commit; merge master; PR with Placement Justification (from 120's report) + publish-size numbers; `gh pr merge N --auto --merge`. 3) After merge: deploy BFF to dev from a fresh short-path worktree of origin/master (`scripts/Deploy-BffApi.ps1`, verify 4/4 SHA-256, /healthz, new route 401), remove the worktree; confirm the add-in deploy for the merge SHA. 4) Owner live checks (round 12) listed below. |

### Uncommitted in the worktree (117-119; 120 adds more)
- 117: `SaveFlow.tsx` (Copy Link = Spaarke `sprk_document` record link via `buildOpenRecordUrl`; hidden without ORG_URL), SaveFlow tests (`buttonFeedback`, `savedState`, `alreadySaved`, new `copyRecordLink`), `notes/117-…md`, `tasks/117-…poml` (120 sets it completed).
- 118: Outlook Quick Save dialog — `shared/commands/{quickSaveDialog.ts,notify.html,readSavedDocument.ts}` (moved from `word/commands/`), `word/commands/index.ts`, `outlook/commands/index.ts` (unfileable/none/404 → save UNFILED + message; info bars removed), `quickSaveHelpers.ts` (`target: null`), `webpack.config.js` (notify page copied for both hosts), tests, `notes/118-…md`, `tasks/118-…poml`.
- 119: `FindResultsList.tsx` (visible open affordance; handlers already wired), new `FindResultsList.openAffordance.test.tsx`, `notes/119-…md`, `tasks/119-…poml`.
- Round note `notes/042-uat-round12-2026-10-08.md`; project `CLAUDE.md` standing block "Multi-customer add-in (Model 1)" + two git/shell gotchas.

### Round 12 live checks for the owner (after deploy)
Outlook (guest, web): Copy Link → Spaarke record link (not `aka.ms/spe-openfilelocation`); reopen a saved email → green "Saved to Spaarke" box + record / "File to record"; Find rows open (web uses a normal tab); Quick Save → progress → success + "Open in Spaarke", contact suggestion / new email → saved UNFILED with the message; an unticked attachment is inside the .eml; pictures in the body render in the .eml? Word: Copy Link = record link; Find rows open.

## Waiting on others
- **UAC-r2**: To Do access-permission column — UAC-r2 replied 2026-10-08: effective access was already correct (PAT-176903 is Restricted, not secure; externals barred through the matter); owner decision (UAC-r2 round 81): a child WITH a parent shows the parent's value, written once in the SHARED stamp path (no Office-only write) and kept in step by the reconcile job; a child without a parent keeps its own. A UAC-r2 task delivers it; **Office needs no change unless the shared path's signature changes** (`notes/119-…md` Part B). Also: #1025 (065 residual gap), share-link route retirement, cross-secure archive copy before StrictDerivedContainer, #1011 repair.
- **customer-provisioning-orchestration-r1**: directory endpoint (240c) → then the add-in's runtime customer selection + first production deploy (`notes/114-…md`). Guest sign-in verified in Outlook on the web. Provisioning asked about multi-tenant: answer is NO (see CLAUDE.md block). Diagnostics view stays on dev until their guest tests finish.
- **Indexing owner**: Find's "Matter: Matter" / "Unknown" labels are server fallbacks (`VisualizationService.cs:836/:1258`).

## Open owner items
- Full UAT (rounds 5-12, Email tab, archive, Test User 1 / 080, filing to a secure record without access) after UAC-r2's secure project.
- 090 wrap-up (`/test-diet`) after UAT. Dev package rename "Spaarke (Dev)" at the next dev package bump. 098 composer record link reaches users when the Email page / Console / upload wizard / communication PCFs are rebuilt.

## Main checkout
`C:\code_files\spaarke` is on master with local changes from another session — leave it; never `git pull` there when not clean/on another branch (memory).
