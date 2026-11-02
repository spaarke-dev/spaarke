# Current Task State

> **Last Updated**: 2026-11-02 (after the 151c-2 merge). work/ebilling-r1 = 151c-1a, 151c-1b and 151c-2 merged, plus master. #2507 is on master (0878ca36ac).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Running** | The post-merge gate on work (5 builds, ArchTests, broad filter including Communication\|Incoming\|ApExport\|Email\|MarkPaid, integration, ApExport and secure-child Jest). Output goes to `w151c2-gate.txt` in the session scratchpad. |
| **Next action** | (1) If the gate is green, push work. ISS-005 is flaky (#2469): re-run it alone. (2) Ask the owner for the live dev runs: the 151c-2 schema (`Deploy-EbillingCommunicationBatchLink.ps1`, which publishes `sprk_communication` only; check for pending sprk_communication form edits first), then `Deploy-EbillingFormsViews.ps1 -FormsOnly`; and the UI import (task 153 scope): code page `sprk_apexport`, ribbon script 1.1.0 `{a20517ef-dd2a-4461-822b-f80c1d735e69}`, the RibbonDiffs on sprk_invoice and sprk_billingexport, the views, and the secure-child ribbon script (`NO_NEW_HOSTS`). Send notices to peers (uac-r2, ontology-r1, SPA-r3, field-mapping) before and after each schema run. (3) The next ebilling master PR, carrying 151c: ontology-r1 review, then before-notices to word-add-in, SPA-r3 and uac-r2, auto-merge, and the SHA to ontology-r1. |

## Done this stretch
- 151c-1b merged into work (b1f05d71c5). 151c-2 merged (9402a48ad4). uac-r2 passed both, including the 151c-2 F1 re-check; I took its doc-line nit. The forms-script conflict kept both the AP emails subgrid and the Sent to AP Date view column.
- Live on dev, 2026-11-02 (owner OK): `sprk_ebillingsetting.sprk_apexportemail` and `sprk_billingexport.sprk_senttoapdate`; the batch form date is read-only.
- Filed: #2511 (SendAsUserAsync post-send token), #2517 (Email to AP: an unknown send outcome is answered as retryable).

## Dev state
- The dev BFF has been on master 3740e1a105 since 2026-11-02 18:52Z (SPA-r3 deployed it; that build includes #2507). It does NOT include 151c. `Ebilling:Enabled=true`, `StoredFileReconcileEnabled=false`, `MalwareScanningEnabled=false`. Before deploying, SPA-r3 ran `Set-ChildRecordCreatorPersonSchema.ps1 -Verify`, which exited 0.
- word-add-in now switches corrections on and bumps the add-in version. Ebilling then tests a correction against dev when asked.
- Not on dev yet: the 151c-2 lookup and relationship, the batch form's AP emails subgrid, the ApExport code page, the 151b and 151c-1b ribbons and views, and the secure-child ribbon change.

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
