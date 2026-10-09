# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. the "Multi-customer add-in (Model 1)" block). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-08. Guest sign-in (#1453): tasks 122 + 124 RUNNING in parallel (agents); 123/125 next; 126 blocked on UAC-r2. Round 12 live in dev (#1431); pane button-wrap fix live (#1454, `b0a78f880`).

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Guest sign-in (#1453). **122 ✅ + 124 ✅** done 2026-10-08 (two verifier passes clean), committed; PR open with auto-merge. Review: `notes/122-guest-signin-review.md`; 124: `notes/124-spedocumentviewer-and-orphans.md`. |
| **Status** | Gates: Spaarke.Auth jest 12/182, build+lint clean; SpeDocumentViewer build:prod PASS, jest 25; add-ins jest 102/1347, tsc prod 0 / 68, lint 0. CI now runs Spaarke.Auth tests (`office-addins-tests.yml` typecheck job). Issues filed: #1453, #1464 (G4 mail as user), #1468-#1470 (orphan controls, cleanup gate, app ids), #1471 (account match on shared browsers). |
| **Now (2026-10-09)** | Task 125 deploy DONE (21 items, versions verified in Dataverse); **owner running the guest/member UAT** (+ 078: one Word-on-the-web run + desktop Word build; 080: Test User 1 live check). Bundle refresh PR #1495 auto-merging. **Task 121** implemented + gates green (uncommitted; office jest 105/1357 with `Spaarke.UI.Components/node_modules` moved aside — a stray second React copy in this worktree breaks 7 suites locally only); agent now adding the owner's rule (2026-10-09): **a pane/Quick Save that reconciles to an existing UNFILED communication files it to the picked record, via the existing filing path; never moves a filed one; never fails the save**. Owner approved **merge + dev BFF deploy** for 121 (2026-10-09). ci-gated-suites: 3 new 121 suites added. Issues: #1505 (EWS id to Graph). |
| **Next Action** | 122/123/124 merged (#1472, #1483 `1f470eb0c`; CVE fix #1475). **Task 125 deploy (done)** (agent, owner go 2026-10-08, from worktree `C:/wt125` at `1f470eb0c`): 10 PCFs, 8 code pages, 3 web resources; preflight checks deployed version < new version (owner: an un-bumped version imports but doesn't load) and post-import `customcontrol.version` = new. **HELD**: SemanticSearchControl (dev 1.1.82 from unmerged #1415), code page `sprk_documentrelationshipviewer` (deployed from another branch 10-08 09:43), Matter form edit. **Deferred, after #1415 merges**: SemanticSearchControl reads `appInsightsKey` env-var-first (it reads only the form today), then remove the static dev `tenantId`/`appInsightsKey` from the Matter main form, then deploy SemanticSearchControl + the DRV code page. Then: guest + member live check with the owner. Then task 126 (after UAC-r2), task 121. |

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
