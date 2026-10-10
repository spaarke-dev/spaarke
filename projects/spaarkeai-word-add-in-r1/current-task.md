# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. the "Multi-customer add-in (Model 1)" block). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-08. Guest sign-in (#1453): tasks 122 + 124 RUNNING in parallel (agents); 123/125 next; 126 blocked on UAC-r2. Round 12 live in dev (#1431); pane button-wrap fix live (#1454, `b0a78f880`).

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Guest sign-in (#1453). **122 ✅ + 124 ✅** done 2026-10-08 (two verifier passes clean), committed; PR open with auto-merge. Review: `notes/122-guest-signin-review.md`; 124: `notes/124-spedocumentviewer-and-orphans.md`. |
| **Status** | Gates: Spaarke.Auth jest 12/182, build+lint clean; SpeDocumentViewer build:prod PASS, jest 25; add-ins jest 102/1347, tsc prod 0 / 68, lint 0. CI now runs Spaarke.Auth tests (`office-addins-tests.yml` typecheck job). Issues filed: #1453, #1464 (G4 mail as user), #1468-#1470 (orphan controls, cleanup gate, app ids), #1471 (account match on shared browsers). |
| **Latest (2026-10-09 eve)** | Held items deployed (SemanticSearchControl 1.1.84, DRV code page). **127 done**, PR #1552 auto-merging (watcher). **Owner approved the 127 rollout (all 4 steps)**: BFF deploy → confirm `/api/config/client` returns the connection string → PCFs SemanticSearchControl 1.1.85 + VisualHost 1.4.40 → code pages DailyBriefing/EmailPage/CommunicationReconciliation → Matter form: remove static `appInsightsKey`+`tenantId` from the SemanticSearchControl binding (3 form factors; back up formxml first) + publish sprk_matter. **Owner decision: external-service-usage = true for every BFF-calling PCF** — agent aligning VisualHost (stays 1.4.40, undeployed), SpeDocumentViewer, CommunicationConnections, RegardingResolver (version bumps) — commit as a follow-up PR; the 127 rollout must deploy VisualHost from a master that includes it. |
| **Running (2026-10-09 pm)** | #1415 merged → held items deploying (agent, worktree `C:/wt125b`, owner-approved 10-08): SemanticSearchControl 1.1.82 → **1.1.84** + code page `sprk_documentrelationshipviewer`. **Matter form edit NOT done**: its static `appInsightsKey` is the only client telemetry source (sprk_ApplicationInsightsKey is a server-side Secret) → #1537 → **task 127** (owner: do it here) RUNNING (agent, uncommitted): `/api/config/client` serves the browser App Insights connection string; AppInsightsService + 5 surfaces use it; form-edit plan in `notes/127-client-telemetry.md` for owner approval. Provisioning T255 (workforce tenant list) merged — fold into 126. |
| **Earlier (2026-10-09)** | **121 merged (#1495 `81e6bbfbb`) and live in dev** (add-in auto-deployed; BFF deployed 4/4 SHA-256, /healthz). Owner: 121 live checks + the guest/member UAT for 125 (+ 078 Word-on-the-web run + desktop Word build; 080 Test User 1 check). |
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
