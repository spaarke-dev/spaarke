# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. the "Multi-customer add-in (Model 1)" block). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-08. UAT round 12 (117-120) merged (#1431, `7bae5950f`), add-in deployed, dev BFF deployed. Waiting on the owner's round-12 live checks. Task 121 open.

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **UAT round 12** (`notes/042-uat-round12-2026-10-08.md`) — B2B guest (`ralph@deweycheatham.onmicrosoft.com`) in Outlook on the web + owner in Word. Tasks 117-120 done and live in dev. |
| **Status** | #1431 merged (`7bae5950f`); add-in deploy for that SHA green; dev BFF deployed from `7bae5950f` (4/4 SHA-256, /healthz Healthy, `POST /api/documents/resolve-email-identity` and `GET /api/documents/{id}/identity` → 401 unauthenticated); deploy worktree removed. 121 (pane email save sends the Exchange item id as internetMessageId) is OPEN. |
| **Next Action** | 1) Owner runs the round-12 live checks (below); fix anything they report. 2) Task 121 (POML ready) — implement via task-execute; it changes the save contract client + server and needs a further owner go for the dev BFF deploy. |

### Round 12 live checks for the owner
Outlook (guest, web): Copy Link → Spaarke record link (not `aka.ms/spe-openfilelocation`); reopen a saved email → green "Saved to Spaarke" box + record / "File to record"; Find rows open (web uses a normal tab); Quick Save → progress → success + "Open in Spaarke", contact suggestion / new email → saved UNFILED with the message; an unticked attachment is inside the .eml; pictures in the body render in the .eml. Word: Copy Link = record link; Find rows open. Note: emails saved from the PANE before 121 store the Exchange item id; the already-saved lookup accepts both keys.

## Waiting on others
- **UAC-r2**: To Do access-permission column — owner decision (UAC-r2 round 81): a child WITH a parent shows the parent's value, written once in the SHARED stamp path and kept in step by the reconcile job; a UAC-r2 task delivers it; **Office needs no change unless the shared path's signature changes** (`notes/119-…md` Part B). Also: #1025 (065 residual gap), share-link route retirement, cross-secure archive copy before StrictDerivedContainer, #1011 repair.
- **customer-provisioning-orchestration-r1**: directory endpoint (240c) → then the add-in's runtime customer selection + first production deploy (`notes/114-…md`). Multi-tenant: NO (CLAUDE.md block). Diagnostics view stays on dev until their guest tests finish.
- **Indexing owner**: Find's "Matter: Matter" / "Unknown" labels are server fallbacks (`VisualizationService.cs:836/:1258`).
- PR #1424 (another session's CS0411 fix for #1418) is redundant — master got the same line via #1411. Left for its owner to close.

## Open owner items
- Full UAT (rounds 5-12, Email tab, archive, Test User 1 / 080, filing to a secure record without access) after UAC-r2's secure project.
- 090 wrap-up (`/test-diet`) after UAT. Dev package rename "Spaarke (Dev)" at the next dev package bump. 098 composer record link reaches users when the Email page / Console / upload wizard / communication PCFs are rebuilt.

## Main checkout
`C:\code_files\spaarke` may be on another branch with another session's changes — leave it; never `git pull` there when not clean/on master (memory).
