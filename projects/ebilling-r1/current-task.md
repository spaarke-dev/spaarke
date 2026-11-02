# Current Task State

> **Last Updated**: 2026-11-01 (checkpoint). **#2507 merged into master at 0878ca36ac.** origin/work/ebilling-r1 = 7b6df6acae (PR head); this checkpoint commit is local.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Running** | uac-r2 is re-checking the 151c-1a fix diff `4e4d45ac23..5bdd25bdc6` on `task/ebill-151c1a-email-route` (worktree `C:\code_files\spaarke\.claude\worktrees\agent-ab1a1dc9a62b799db`). The merge into work waits for its verdict. |
| **Next action** | (1) If uac-r2 passes 151c-1a: merge it into work, then gate (5 builds, ArchTests, broad filter plus `Communication\|Incoming\|ApExport`, integration), then push. If it raises findings, fix them on the branch. (2) After uac-r2 sends "move done": send notices, then run the two column scripts on dev (`sprk_apexportemail`, `sprk_senttoapdate`) and `Deploy-EbillingFormsViews.ps1`, then the -Verify scripts below. (3) Next tasks: 151c-1b (UI) and 151c-2 (the `sprk_billingexport` reference on `sprk_communication`). (4) When a dev BFF deploy includes 0878ca36ac (SPA-r3 or another team), tell word-add-in. |

## Done this stretch
- #2507 merged into master (0878ca36ac): 097a–f, 101, 104, 150, 151a/b, 152, 191, 192, the resubmissionof field security and the 049 seed owner. ontology-r1 approved after round 1 (F1 supersede fix; K1 stored decider, with the fallback to the caller accepted as a known limit; K2 test).
- #2506 (D134) was a master test-seed defect, fixed by uac-r2's #2509.
- Filed #2511: shared `SendAsUserAsync` runs its post-send writes on the request token.

## 151c-1a (head 5bdd25bdc6, not on work)
- Routes `POST /api/finance/export-batches/{id}/email` and `.../mark-sent-to-ap` now need Read+Write on the batch (EB-D-183).
- Owner check (F4): send refused unless the resolver's answer = the batch's owning team.
- Re-ingestion: 409 when a recipient is a monitored mailbox. A signed `x-spaarke-server-send` marker, bound to a digest of the sent files, makes inbound skip attachment-text matching, documents and the .eml archive.
- 3 MB attachment budget; refusals below 500 return a non-retry 409; the date write uses `CancellationToken.None`; the date is read-only on the form.
- E1 exception: EB-D-184. Cite it in the 151c PR.
- The shared inbound processor changed. Mention it to the email-intelligence owners in the 151c PR.

## Dev state
- **Hold:** uac-r2 is moving the dev users and both BFF application users into BU1 (owner-approved). Hold the 151c schema runs until uac-r2 sends "move done", or agree a time with them first. After "move done", run the -Verify scripts: `Set-EbillingFindingsJsonFieldSecurity`, `Set-EbillingFirmReasonFieldSecurity`, `Set-EbillingResubmissionOfFieldSecurity`, and `Seed-BillingSystemPolicy -Verify -RequireRows -CustomerBusinessUnit 'Spaarke Business Unit 1'`.
- The dev BFF does not include #2507 yet. SPA-r3 will run `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` (must exit 0) before its next deploy and will check our six keys. The ApExport code page and the 151b ribbons and views are NOT deployed (the dev import needs the owner's OK).

## Open items
- The main checkout `C:\code_files\spaarke` has another session's uncommitted changes, so its master was not fast-forwarded. Leave it.
- Older notes name the owner in plain text (003, 004, 011, 029, 091). Clean up in a docs pass.
- 151b: 053 must add `sprk_trackingnumber` to `sprk_Invoice/Entity.xml` before any non-dev import.
- 050a §9 items 2–4 (owner).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices (warn of ~1 min of invalid-column faults on column deletes). StoredFileReconcileJob stays off until F4 merges and DEF-044 is fixed. After merges: 5 builds, ArchTests, broad filter before pushing (ISS-005 flaky, #2469). If builds report a locked file, run `dotnet build-server shutdown` and rebuild, but never while another gate is building. Master PRs: auto-merge only after ontology-r1 approves; send before-notices to word-add-in, SPA-r3 and uac-r2, and send the merge SHA to ontology-r1. Max 4 concurrent subagents; every agent launch states a model. No bare jq, no grep -P. Remove worktrees with `pwsh -File scripts/worktree/Remove-WorktreeSafe.ps1 -Path <one path>`.
