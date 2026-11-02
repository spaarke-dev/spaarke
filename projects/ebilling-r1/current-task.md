# Current Task State

> **Last Updated**: 2026-11-02 (checkpoint, context ~86%). origin/work/ebilling-r1 = 7d493b53db (151c-1a merged, gated green). #2507 is on master (0878ca36ac).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Running** | (1) An adversarial review (opus) of 151c-2, `7d493b53db..e1a8be6223` on `task/ebill-151c2-batch-link` (worktree `C:\code_files\spaarke\.claude\worktrees\agent-ab1007764e5ac3856`). (2) uac-r2's access review of the same diff (requested). |
| **Next action** | (1) 151c-1b: review and fixes are done (head 6049c26f65 on `task/ebill-151c1b-email-ui`, worktree `...agent-ae423c3eb3b722197`; F1/F2/F3 fixed, K1/K2/K6 done, Jest 166/0). I checked the F1 diff: one sender per batch in an `ExportDialog` ref map, and the button is disabled when locked. **Merge it into work, run the gate (5 builds, ArchTests, broad filter + Communication\|Incoming\|ApExport\|Email, integration), push.** (2) 151c-2: when both reviews are in, fix any F findings, merge, gate, push. Then send notices and run on dev: `Deploy-EbillingCommunicationBatchLink.ps1 -WhatIf`, then for real (it publishes `sprk_communication` only; check for pending sprk_communication forms first), then `Deploy-EbillingFormsViews.ps1 -FormsOnly`. (3) Ask the owner for the dev import of the 151b and 151c-1b UI: code page `sprk_apexport`, ribbon script 1.1.0 (`{a20517ef-dd2a-4461-822b-f80c1d735e69}`), the RibbonDiffs on sprk_invoice and sprk_billingexport, and the views. At import, check whether the Office Add In User role needs Read on `sprk_billingexport` (151c-1b K3). (4) Follow-up task: the invoice form shows the batch's Sent to AP Date (EB-D-182 (3); 151c-1b K4). |

## Done this stretch
- #2507 merged into master (0878ca36ac). ontology-r1 and uac-r2 passed it, including K1. word-add-in has the SHA. The dev BFF does not have it yet: SPA-r3 will run `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` (must exit 0) before its next deploy, and will tell us and word-add-in.
- 151c-1a merged into work (60e19c8fbe). uac-r2 passed it after 2 rounds. EB-D-183 (Write on the batch; no re-ingestion) and EB-D-184 (narrow E1 exception) are recorded.
- Live on dev, 2026-11-02 (owner OK, notices sent): `sprk_ebillingsetting.sprk_apexportemail` and `sprk_billingexport.sprk_senttoapdate`; the batch form date is read-only. The publish was entity-scoped.
- uac-r2's BU1 move is done, and the four -Verify scripts pass after it.
- Filed: #2506 (fixed by #2509), #2511 (shared SendAsUserAsync post-send token), #2517 (Email to AP: an unknown send outcome is answered as retryable).

## Dev state
- The BFF on dev does not include #2507 or 151c. `Ebilling:Enabled` is on (owner); `StoredFileReconcileEnabled=false`.
- The ApExport code page and the 151b/151c-1b ribbons and views are NOT deployed (needs the owner's OK).

## Open items
- The main checkout `C:\code_files\spaarke` has another session's uncommitted changes, so its master was not fast-forwarded. Leave it.
- Older notes name the owner in plain text (003, 004, 011, 029, 091). Clean up in a docs pass.
- 151b: 053 must add `sprk_trackingnumber` to `sprk_Invoice/Entity.xml` before any non-dev import.
- 050a §9 items 2–4 (owner).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices before and after, and the publish is entity-scoped only (ontology-r1). StoredFileReconcileJob stays off until F4 merges and DEF-044 is fixed. After merges: 5 builds, ArchTests, broad filter before pushing (ISS-005 flaky, #2469). Never run `dotnet build-server shutdown` while another gate is building. Master PRs: auto-merge only after ontology-r1 approves; send before-notices to word-add-in, SPA-r3 and uac-r2, and send the merge SHA to ontology-r1. Max 4 concurrent subagents; every agent launch states a model. No bare jq, no grep -P. Remove worktrees with `pwsh -File scripts/worktree/Remove-WorktreeSafe.ps1 -Path <one path>`.
