# Current Task State

> **Last Updated:** 2026-11-23 (checkpoint: owner answered 050a/089/M2; next is 050c, 089 close-out, 053 → 079 → 112)
> Older detail: `notes/handoff-history/current-task-2026-11-20a.md` (the previous state, verbatim), plus `-2026-11-16a`, `-15a` and `-13a`.

## Owner mode

- "Continue with open work items that are available." Keep `notes/owner-testing-checklist.md` current.
- The owner works over Remote Control. The closing message says what was done and what failed, plus the decisions needed, as questions with reply choices.

## Quick recovery

- **Owner answers 2026-11-23 (record them in the notes; not yet recorded except M2):**
  - **050a Q2 = (b):** allow Reject of a confirmed document only after its submission ended **Failed** (no invoice). After a Rejected submission an invoice exists, so the document is linked and reject stays 409.
  - **050a Q3 = (b):** drop `InvoiceDate` and `TotalAmount` from the confirm request contract AND from the Invoice Review page.
  - **050a Q4:** the owner said "a user (which can be a reviewer) can manually create an invoice; we need a manual invoice create option; whichever option supports this". Manual create today = CreateInvoiceWizard (052) → `POST /api/finance/{core}/{id}/invoice-submissions`, which checks `finance.submit_invoice` = Read + AppendTo on the core record, NOT Create on sprk_invoice (`OperationAccessPolicy.cs:195-201`). Confirm checks Write on the document plus a Create-on-sprk_invoice table privilege. So **(b)**: confirm uses the same check as the manual submit. This keeps working after 096b D3 removes user Create on sprk_invoice. Tell the owner that a "manual create" with NO PDF (typed invoice) does not exist today; ask whether it is needed.
  - **089 Q4 = (a):** keep allowing "Change for this record" on a rule that a newer Procedure version dropped (flexibility, rare case). No code change; record it as a decision.
  - **089 Q5 = (b):** the 089d live child-unit proof waits for the first customer with child units. The owner notes that billing admins generally have access to all rules.
  - **M2:** wait for 112 (recorded, 02d3477efc).
  - **112:** "ok; what is holding up 079? proceed".
- **Exact next actions:**
  1. Record the answers: 050a notes §9, 089 notes (K1 row 4 and 089d), decisions-log (new EB-D ids), and the TASK-INDEX rows 050a and 089. 089 can then close ✅. Its stale-Signals close stays a known limit; the message to the reviewer says so.
  2. Create POML **050c** (050a follow-ups Q2/Q3/Q4: BFF confirm/reject, the Invoice Review page, tests; FULL, sonnet/high; uac-r2 reviews the privilege change). Run it through an implementer, then an Opus review.
  3. **079 is blocked only by 053** (WP2 deploy; steps 2-3 done 2026-11-03; steps 1 and 4-13 open: app settings, field security script, BFF, route smoke, SPA, solution source, end to end). 089 closes with (1). Then 053 → 079 → 112.
     - The deploy model has changed since the POMLs were written: dev BFF builds come from master, by uac-r2, on the owner's exact-SHA go in uac-r2's session.
     - 053 step 4 names retired settings (ProcedureLibraryContainerId and EbillingOutputContainerId are retired by 092 and 124). Re-plan 053's remaining steps against the current state before running anything live.
     - Live Dataverse writes need the owner's typed OK in this session plus uac-r2 notices.
- **Master PR #3029:** auto-merge ON (owner go 2026-11-23), merge commit, pinned to head e734071ba5. ontology-r1 approved; uac-r2 096 check PASS. **Do NOT push work/ebilling-r1 until #3029 merges.** Local commits are waiting: 02d3477efc plus this checkpoint. After the merge, uac-r2 builds the dev BFF from master and sends the live notice to us and word-add-in-r1 (096 range check).
- **Dev BFF a48398746d ✅** (uac-r2, carries #2992 = 117/123/124/193). The gate passed; the 123 check count was 0, so K1 is closed.
- **Stays off:**
  - 117's Apply stays off until 079 (owner OK). Precondition #2982 is FIXED on master (#3011, 33d7c6a6ad); it holds once the deployed BFF contains that commit.
  - `Ebilling:StoredFileReconcileEnabled` stays false.
- **Uncommitted files:** none (the checkpoint is committed locally, not pushed).
- **Machine load:** BFF unit runs time out (3-minute per-test timeouts) under load. Rerun the failing classes with `-- xUnit.MaxParallelThreads=4`.
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
