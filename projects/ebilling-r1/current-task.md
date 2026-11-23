# Current Task State

> **Last Updated:** 2026-11-23 (050b, 124, 086 and 123 complete). Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-20a.md` (the previous state, verbatim), plus `-2026-11-16a`, `-15a` and `-13a`.

## Owner mode

- "Continue with open work items that are available." Keep `notes/owner-testing-checklist.md` current.
- The owner works over Remote Control. The closing message says what was done and what failed, plus the decisions needed, as questions with reply choices.

## Quick recovery

- **Task 193 (parallel):** owner go to build typed 2026-11-23 ("Yes proceed with those items"); build agent on `task/ebilling-193` from 7a6c530000. The dev -Apply (live column delete) needs a SECOND owner go plus uac-r2 notices; uac-r2 reviews the PR first.
- **Active task: 117** (FULL, opus @ high; started 2026-11-23). Build agent on `task/ebilling-117` from b8e6e686c0. uac-r2 CONFIRMED the five delete-list points and added: an age floor (modifiedon margin), the assignment-delete cascade direction check, the Restrict check (§15) and rate-card evidence (dev still has the three RemoveLink core lookups; 085 stopped reading them). Scope also deletes orphan assignment rows (uac-r2 design §14). Next: review (2 Opus passes, tenant isolation), then uac-r2 review of branch + SHA before merge. 096 and 121 stay gated.
- **123 deploy condition (uac-r2 condition 2):** 123's BFF change goes into a LATER dev deploy (not 9df4e5c819). After that deploy, run the read-only dev check in `notes/123-invoice-supporting-file-upload.md` §12 and send uac-r2 the count (ids by message only).
- `Ebilling:StoredFileReconcileEnabled` stays false: its flip needs F4 merged, DEF-044 fixed and the owner's go through uac-r2.
- **Held for the PR to master:** spa-r4 reviews 134's external-spa diff (`git diff 19866f6606 5f7195b9eb -- src/client/external-spa`). Owner chose "Merge, review later" on 2026-11-21. spa-r4's session is not running; send it the diff when it is.
- **096 gate:** word-add-in-r1 slice C (PR #2927, merge 148ec4dec0) is live on dev since 2026-11-21. 096 still waits for one add-in release after it (POML gate), then removes `POST /api/office/quickcreate/invoice` and the Invoice target of `/api/office/save`.
- **Waiting on the owner:** the OK to redeploy the Invoice Review page to dev (checklist M2).
- **SpaarkeMaster hotfix (2026-11-22, owner chose "Fix in source"):** 1.3.1.0 and 1.3.2.0 could not be imported because the Invoice form (151e) names `sprk_invoicefiles` and the package lacked it. PR #2935 adds the page, bumps to 1.3.2.1, and adds the publish check `Find-MissingWebResourceReferences`. ✅ Merged 2026-11-22 (bc4565a1b5) and published (run 45037908496; latest.json read back as 1.3.2.1). provisioning-r1 has been told; C:\wtsm has been removed. Dev and ACME imports are the owner's call and provisioning-r1's work, not ours. Task 153 no longer needs to add the web resource to the package; it still deploys it to dev.
- **Uncommitted files:** none, after the 134 bookkeeping commit.

## Recently done (detail in the notes and git)

- **123 ✅** merged into work 72c416e948 (branch `task/ebilling-123`, tip 5bb6965b77). POST /api/finance/invoices/{invoiceId}/files (internal supporting-file upload) plus a SubmissionCommitGate change (Invoice PDF flipped last; early Passed once Servable). Two Opus passes, fix rounds 1-2, scoped re-check K-only; uac-r2 approved (condition 2 open at deploy); word-add-in-r1 agreed the contract. ISS-043 #2978 filed in passing. Notes: `notes/123-invoice-supporting-file-upload.md`.

- **086 ✅** closed 2026-11-22. Pre-deploy count 0, both -Verify PASS, NFR-05 has no sprk_storedfile finding, live dev BFF `c8ab2cba9e` contains it, uac-r2 signed off, owner accepted -Verify for criterion 6. Notes: `notes/086-storedfile-secure-child.md`.

- **124 ✅** merged into work 3b7367bfbf (branch `task/ebilling-124`, tip decb4de05f). Finance:ProcedureLibraryContainerId retired everywhere; the uac-r2 191b library hide step and the #2151 declared-home mechanism removed with uac-r2 agreement; route census ArchTest (whole Ciam call closure) and a Procedure Stored File deny proof added. Two Opus passes + fix round 1; uac-r2 APPROVED decb4de05f. Any dev BFF deploy carrying 124 goes through uac-r2 on the owner naming the exact SHA. #1967 closed, provisioning-r1 told. Notes: `notes/124-retire-procedure-library-key.md`.
- **050b ✅** merged into work 18b02cbdcf (branch `task/ebilling-050b`, tip b0be013e48). Deleted the legacy extraction path, `OutputOrchestratorService` and the `finance.extraction.*` metrics. One Opus review: no F1/F3, five F2 fixed in round 1. Closed #1918, #1919; reopened #2263 (Optimistic-mode defect, now off every live path). Notes: `notes/050b-retire-legacy-extraction.md`; checklist section P. Send the PR to ontology-r1 at the master PR (POML). The agent worktree has been removed.
- **134 ✅** merged into work 5f7195b9eb (branch `task/ebilling-134`, tip b29a9e7c9a). Outcome section in the invoice record modal plus the same report on the submit result screen; print through a body-level portal (EB-D-254). Opus review (F1 print truncation, F4 reason code: fixed) and uac-r2 PASS. Notes: `notes/134-spa-invoice-outcome.md`; checklist section O. A first post-merge vitest run had 16 failures under heavy machine load; the rerun passed 1881/0.
- **130g ✅** merged into work 6b84fb1db1. Notes: `notes/130g-firm-view-notice-prose.md`.
- **132ui ✅** merged into work 04718b3224. ISS-041 #2930 filed. Checklist M2 waits for the page deploy.
- **131 dev seed ✅** 2026-11-19 (EB-D-256). `/healthz/catalog` stays 503 because of pre-existing drift (#1987).

## Open work

- **Build:** 117, 096 (gated, see above), 121 (waits for uac-r2 191).
- **Partly done:**
  - 164: the live `-Apply` needs the owner's go.
  - 089 and 050a: owner questions are in their notes.
- **Deploy close-outs:** 053, 079, 112, 136, 153. Then 160, 161/162 (live runs need the owner's OK), 163 UAT, 189, 190.
- **Index tidy-up:** the drift check lists 028, 040, 050a, 084d, 093, 113 and 161. These are pre-existing; resolve each from evidence.

## Standing reminders

- **Live Dataverse writes or role changes:**
  - Need the owner's OK, typed in THIS session.
  - uac-r2 gets a notice before and after.
  - Private access findings go to uac-r2 by message only, never into the public repo.
- **Commands:**
  - Never run `python`, `python3` or bare `python`.
  - No `git stash`, no `--no-verify`.
  - Merge master PRs as merge commits.
  - Never run the whole `Sprk.Bff.Api.Tests` or the whole ArchTests; filter them.
- **Agents:**
  - At most 4, each with a named model.
  - A worktree agent starts on master, so its brief names the base sha.
  - Remove agent worktrees with `scripts/worktree/Remove-WorktreeSafe.ps1`. The 132ui, 130g and 134 agent worktrees have been removed.
- **Clean-up:** a human should delete C:\wt131a and C:\wt131b. The 124 fix-round agent worktree `C:\code_files\spaarke\.claude\worktrees\agent-accb6a7742c6a7c1b` is clean but locked by the desktop app; remove it with Remove-WorktreeSafe.ps1 once the lock is gone.
