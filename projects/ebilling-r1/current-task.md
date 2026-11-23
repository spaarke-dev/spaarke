# Current Task State

> **Last Updated:** 2026-11-23 (checkpoint: 194 + 096 agents running; owner questions open)
> Older detail: `notes/handoff-history/current-task-2026-11-20a.md` (the previous state, verbatim), plus `-2026-11-16a`, `-15a` and `-13a`.

## Owner mode

- "Continue with open work items that are available." Keep `notes/owner-testing-checklist.md` current.
- The owner works over Remote Control. The closing message says what was done and what failed, plus the decisions needed, as questions with reply choices.

## Quick recovery

- **Running now (agents, background):**
  - **194 ✅** merged 96bcf65dbb; dev forms cleaned, -Verify PASS, uac-r2 notified before and after.
  - **096 ✅** merged into work 98801e9b9f (Opus review: F2 fixed f60f51a2a3); word-add-in-r1 no objection. Tell word-add-in-r1 when it is on the dev BFF (range check).
- **Exact next actions:**
  1. When an agent reports: Opus adversarial review (one pass for 194; 096 is bff-api, one pass), fix rounds, a scoped re-check, then merge `--no-ff` into work.
  2. **194:** uac-r2 BEFORE notice, then `-Apply` on dev (owner go typed 2026-11-23: "yes proceed with these"), then `-Verify` and the AFTER notice.
  3. **096:** send word-add-in-r1 the merge SHA; it range-checks the change once it is on the dev BFF.
- **Owner questions asked 2026-11-23 (waiting on answers):**
  - 050a §9 items 2-4: reject after confirm; drop InvoiceDate/TotalAmount from the confirm request; the confirm privilege.
  - 089: refuse "change rule" on a rule a newer Procedure version dropped (record not pinned); 089d live child-BU proof on dev, now or later.
  - M2: the Invoice Review page has NEVER been on dev. Wait for task 112, or a page-only upload after the next dev BFF?
- **Master PR #3029 OPEN (2026-11-23):** 194 + 096 + ontology-r1 follow-ups; ontology-r1 asked to review; merge needs the owner's go in THIS session. After merge: uac-r2 builds dev BFF from master (the deploy go is the owner naming the exact SHA in uac-r2's session, not here) and checks 096's OFFICE_023 first; uac-r2 sends the live notice to us and word-add-in-r1.
- **Master PR #2992 ✅ merged 2026-11-23 16:35Z (2d83b246ae).** ontology-r1s follow-ups (cd485c6c5e, 71496ac36c, dc628e7152) go in the NEXT eBilling PR.
- **164 ✅ 2026-11-23:** dev was already in the target state; -Verify and the SecureChild -Verify PASS; uac-r2 acked.
- **Dev BFF a48398746d ✅ (uac-r2, 2026-11-23 18:43Z, carries #2992 = 117/123/124/193):** gate PASSED 18:43–19:11Z. 123 check count 0, K1 closed (notes/123 §12). DynamicCommandResolver exceptions are pre-existing (#3005, uac-r2).
- **Stays off:**
  - 117s Apply stays off until 079 (owner OK). Precondition #2982 is FIXED on master (#3011, 33d7c6a6ad): it holds once the deployed BFF contains that commit.
  - `Ebilling:StoredFileReconcileEnabled` stays false (F4, DEF-044, owner go via uac-r2).
- **Uncommitted files:** none.
## Recently done (detail in the notes and git)

- **193 ✅** merged into work c21bde55a2 (branch `task/ebilling-193`, tip 46a89e55f8). sprk_ratecard's sprk_matter, sprk_project and sprk_workassignment lookups and their 3 relationships were retired from the source, the schema scripts and the docs.
  - Reviews: Opus pass and 2 fix rounds; uac-r2 APPROVED.
  - Dev -Apply ran 2026-11-23 on the owner's typed go, with uac-r2 notices before and after (uac-r2 confirmed it independently). -Verify passed.
  - provisioning-r1 told: other environments lose the columns only through a SpaarkeMaster MANAGED UPGRADE, with the report mode run first on each target.
  - Notes: `notes/193-retire-ratecard-core-lookups.md`.

- **117 ✅** merged into work f4a272136c (branch `task/ebilling-117`, tip becd5b983c). RecordRuleSetRetireJob (O-17 sweep):
  - What it does: retires unlinked record-scoped Billing rule sets (policies switched Off first) and deletes orphan assignment rows.
  - When it writes: only with Apply on; it skips while eBilling is off; age floor 15 min.
  - Reviews: two Opus passes, fix round 1, scoped re-check K-only; uac-r2 APPROVED, including the delete-guard and census-waiver changes.
  - Found in passing: ISS-044 #2982, handed to ontology-r1 (DI-162).
  - Notes: `notes/117-core-record-delete-procedures.md`.
- **#2981 ✅** flaky 084d-3 interleaving test made deterministic (668c0b92d4), closed.

- **123 ✅** merged into work 72c416e948 (branch `task/ebilling-123`, tip 5bb6965b77). POST /api/finance/invoices/{invoiceId}/files (internal supporting-file upload) plus a SubmissionCommitGate change (Invoice PDF flipped last; early Passed once Servable). Two Opus passes, fix rounds 1-2, scoped re-check K-only; uac-r2 approved (condition 2 open at deploy); word-add-in-r1 agreed the contract. ISS-043 #2978 filed in passing. Notes: `notes/123-invoice-supporting-file-upload.md`.

- **086 ✅** closed 2026-11-22. Pre-deploy count 0, both -Verify PASS, NFR-05 has no sprk_storedfile finding, live dev BFF `c8ab2cba9e` contains it, uac-r2 signed off, owner accepted -Verify for criterion 6. Notes: `notes/086-storedfile-secure-child.md`.

- **124 ✅** merged into work 3b7367bfbf (branch `task/ebilling-124`, tip decb4de05f). Finance:ProcedureLibraryContainerId retired everywhere; the uac-r2 191b library hide step and the #2151 declared-home mechanism removed with uac-r2 agreement; route census ArchTest (whole Ciam call closure) and a Procedure Stored File deny proof added. Two Opus passes + fix round 1; uac-r2 APPROVED decb4de05f. Any dev BFF deploy carrying 124 goes through uac-r2 on the owner naming the exact SHA. #1967 closed, provisioning-r1 told. Notes: `notes/124-retire-procedure-library-key.md`.
- **050b ✅** merged into work 18b02cbdcf (branch `task/ebilling-050b`, tip b0be013e48). Deleted the legacy extraction path, `OutputOrchestratorService` and the `finance.extraction.*` metrics. One Opus review: no F1/F3, five F2 fixed in round 1. Closed #1918, #1919; reopened #2263 (Optimistic-mode defect, now off every live path). Notes: `notes/050b-retire-legacy-extraction.md`; checklist section P. Send the PR to ontology-r1 at the master PR (POML). The agent worktree has been removed.
- **134 ✅** spa-r4 review fixed (F1, F4) and re-checked OK; fix merged into work 7600c21f65. Originally merged into work 5f7195b9eb (branch `task/ebilling-134`, tip b29a9e7c9a). Outcome section in the invoice record modal plus the same report on the submit result screen; print through a body-level portal (EB-D-254). Opus review (F1 print truncation, F4 reason code: fixed) and uac-r2 PASS. Notes: `notes/134-spa-invoice-outcome.md`; checklist section O. A first post-merge vitest run had 16 failures under heavy machine load; the rerun passed 1881/0.
- **130g ✅** merged into work 6b84fb1db1. Notes: `notes/130g-firm-view-notice-prose.md`.
- **132ui ✅** merged into work 04718b3224. ISS-041 #2930 filed. Checklist M2 waits for the page deploy.
- **131 dev seed ✅** 2026-11-19 (EB-D-256). `/healthz/catalog` stays 503 because of pre-existing drift (#1987).

## Open work

- **Build:** 096 (gated, see above), 121 (waits for uac-r2 191).
- **Partly done:**
  - 164 ✅ 2026-11-23 (dev already in the target state; -Verify PASS).
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
