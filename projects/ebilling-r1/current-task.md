# Current Task State

> **Last Updated:** 2026-11-21 (134 complete). Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-20a.md` (the previous state, verbatim), plus `-2026-11-16a`, `-15a` and `-13a`.

## Owner mode

- "Continue with open work items that are available." Keep `notes/owner-testing-checklist.md` current.
- The owner works over Remote Control. The closing message says what was done and what failed, plus the decisions needed, as questions with reply choices.

## Quick recovery

- **Active task:** none. 134 closed 2026-11-21. Next is the first open, ungated task in `tasks/TASK-INDEX.md` (050b), run through task-execute.
- **Held for the PR to master:** spa-r4 reviews 134's external-spa diff (`git diff 19866f6606 5f7195b9eb -- src/client/external-spa`). Owner chose "Merge, review later" on 2026-11-21. spa-r4's session is not running; send it the diff when it is.
- **096 gate:** word-add-in-r1 slice C (PR #2927, merge 148ec4dec0) is live on dev since 2026-11-21. 096 still waits for one add-in release after it (POML gate), then removes `POST /api/office/quickcreate/invoice` and the Invoice target of `/api/office/save`.
- **Waiting on the owner:** the OK to redeploy the Invoice Review page to dev (checklist M2).
- **SpaarkeMaster hotfix (2026-11-22, owner chose "Fix in source"):** 1.3.1.0 and 1.3.2.0 could not be imported because the Invoice form (151e) names `sprk_invoicefiles` and the package lacked it. PR #2935 adds the page, bumps to 1.3.2.1, and adds the publish check `Find-MissingWebResourceReferences`. ✅ Merged 2026-11-22 (bc4565a1b5) and published (run 45037908496; latest.json read back as 1.3.2.1). provisioning-r1 has been told; C:\wtsm has been removed. Dev and ACME imports are the owner's call and provisioning-r1's work, not ours. Task 153 no longer needs to add the web resource to the package; it still deploys it to dev.
- **Uncommitted files:** none, after the 134 bookkeeping commit.

## Recently done (detail in the notes and git)

- **134 ✅** merged into work 5f7195b9eb (branch `task/ebilling-134`, tip b29a9e7c9a). Outcome section in the invoice record modal plus the same report on the submit result screen; print through a body-level portal (EB-D-254). Opus review (F1 print truncation, F4 reason code: fixed) and uac-r2 PASS. Notes: `notes/134-spa-invoice-outcome.md`; checklist section O. A first post-merge vitest run had 16 failures under heavy machine load; the rerun passed 1881/0.
- **130g ✅** merged into work 6b84fb1db1. Notes: `notes/130g-firm-view-notice-prose.md`.
- **132ui ✅** merged into work 04718b3224. ISS-041 #2930 filed. Checklist M2 waits for the page deploy.
- **131 dev seed ✅** 2026-11-19 (EB-D-256). `/healthz/catalog` stays 503 because of pre-existing drift (#1987).

## Open work

- **Build:** 050b, 123, 124, 117, 096 (gated, see above), 121 (waits for uac-r2 191).
- **Partly done:**
  - 086.
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
- **Clean-up:** a human should delete C:\wt131a and C:\wt131b.
