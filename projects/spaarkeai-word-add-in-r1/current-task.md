# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. the new "Multi-customer add-in (Model 1)" block). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-08. UAT round 12: 116 live; 117-120 done, gates green, committed — PR → merge → dev BFF deploy (owner approved) → owner live checks; then task 121.

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **UAT round 12** (`notes/042-uat-round12-2026-10-08.md`) — B2B guest (`ralph@deweycheatham.onmicrosoft.com`) in Outlook on the web + owner in Word. Tasks 117-120. |
| **Status** | 116 merged (#1403). 117-120 DONE and committed on the branch (jest 102/1347, lint 0, tsc prod 0 / debt 68, BFF build 0/0, office scope 4830/0, publish +2,846 B 192=192). 121 (pane email save sends the Exchange item id as internetMessageId — found by 120) is OPEN. |
| **Next Action** | 1) Merge the round-12 PR (`gh pr merge N --auto --merge`); confirm the add-in deploy for the merge SHA. 2) Deploy the BFF to dev from a fresh short-path worktree of origin/master (`pwsh scripts/Deploy-BffApi.ps1` from that worktree; verify 4/4 SHA-256, /healthz, `POST /api/documents/resolve-email-identity` → 401 unauthenticated); remove the worktree. Owner approved this deploy 2026-10-08. 3) Hand the owner the round-12 live checks (below + `notes/120-…md`). 4) Task 121 (POML ready) — needs a further dev BFF deploy go. |

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
