# Current Task State

> **Last Updated**: 2026-11-02 (#2526 merged). 151c is on master at 5796774091; #2507 at 0878ca36ac. This session is ebilling-r1-7b; the duplicate 9c session after the crash should stay idle or be closed.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Waiting on** | (a) SPA-r3's dev BFF deploy of 5796774091 (needs the owner's go in SPA-r3's session). (b) The owner's correction test in Outlook (no browser here): first a new submission, then a correction against its invoice. Then ebilling verifies the server side by tracking number. |
| **Next action** | (1) Done: #2526 merged; SHA sent to ontology-r1, uac-r2, SPA-r3 and word-add-in. (2) After that BFF deploy, import the UI on dev (owner OK given 2026-11-02): code page `sprk_apexport`, ribbon script 1.1.0 `{a20517ef-dd2a-4461-822b-f80c1d735e69}`, the RibbonDiffs on sprk_invoice and sprk_billingexport, and the views, through Import-SolutionScoped or the code-page-deploy and web-resource scripts. Check the Office Add In User role's Read on `sprk_billingexport` (K3). This is step 5 of task 153; the rest of 153 (index, end to end, UI tests) waits for task 136. Task 153 step 4 conflicts with the rule that only SPA-r3 deploys the dev BFF: amend the POML. (3) Click-through: a batch's AP emails grid shows no New button (Add Existing may show: K1). This needs a browser; it is on the owner's checklist through uac-r2. (4) Delete `C:\w15p`, the publish-size scratch folder. A safety check blocked my rm, so the owner deletes it. |

## Done this stretch
- 151c-1b merged into work (b1f05d71c5). 151c-2 merged (9402a48ad4). uac-r2 passed both, including the 151c-2 F1 re-check; I took its doc-line nit. The forms-script conflict kept both the AP emails subgrid and the Sent to AP Date view column.
- Live on dev, 2026-11-02 (owner OK): `sprk_ebillingsetting.sprk_apexportemail` and `sprk_billingexport.sprk_senttoapdate`; the batch form date is read-only.
- Filed: #2511 (SendAsUserAsync post-send token), #2517 (Email to AP: an unknown send outcome is answered as retryable).

## Dev state
- The dev BFF has been on master 3740e1a105 since 2026-11-02 18:52Z (SPA-r3 deployed it; that build includes #2507). It does NOT include 151c. `Ebilling:Enabled=true`, `StoredFileReconcileEnabled=false`, `MalwareScanningEnabled=false`. Before deploying, SPA-r3 ran `Set-ChildRecordCreatorPersonSchema.ps1 -Verify`, which exited 0.
- word-add-in now switches corrections on and bumps the add-in version. Ebilling then tests a correction against dev when asked.
- Live on dev 2026-11-02 (owner OK; notices sent before and after): `sprk_communication.sprk_billingexport` and its relationship (cascade read back as agreed); `sprk_/scripts/secure_child_ribbon.js` at the work version (NO_NEW_HOSTS, plus the ISS-010 catch-up), published on its own; the batch form's AP emails subgrid. The click-through check (no New on the subgrid) is still open because no browser is connected.
- Not on dev yet: the ApExport code page, the 151b and 151c-1b ribbons (sprk_invoice and sprk_billingexport RibbonDiffs, the ribbon script `{a20517ef-dd2a-4461-822b-f80c1d735e69}`) and the views (task 153, owner OK given).

## Open items
- 151c-1b K3: the Office Add In User role may lack Read on `sprk_billingexport`. Check when importing the invoice views.
- 151d (new): the invoice form shows the batch's Sent to AP Date (151c-1b K4).
- The main checkout `C:\code_files\spaarke` has another session's uncommitted changes. Don't fast-forward it.
- Older notes name the owner in plain text (003, 004, 011, 029, 091). Clean up in a docs pass.
- 151b: 053 must add `sprk_trackingnumber` to `sprk_Invoice/Entity.xml` before any non-dev import.
- 050a §9 items 2–4 (owner).
- Worktrees to remove once merged and pushed: `C:\code_files\spaarke\.claude\worktrees\agent-ab1007764e5ac3856` (151c-2) and the 151c-1b one, `agent-ae423c3eb3b722197` (`Remove-WorktreeSafe.ps1`, one path per call).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK. Schema runs need notices before and after, and the publish is entity-scoped only (ontology-r1). StoredFileReconcileJob stays off until F4 merges and DEF-044 is fixed. After merges: 5 builds, ArchTests and the broad filter before pushing (ISS-005 is flaky, #2469). Never run `dotnet build-server shutdown` while another gate is building. Master PRs: auto-merge only after ontology-r1 approves; send before-notices to word-add-in, SPA-r3 and uac-r2, and send the merge SHA to ontology-r1. Max 4 concurrent subagents, and every agent launch states a model. No bare jq, no grep -P. Remove worktrees with `pwsh -File scripts/worktree/Remove-WorktreeSafe.ps1 -Path <one path>`.
