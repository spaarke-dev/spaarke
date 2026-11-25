# Current Task State

> **Last Updated:** 2026-11-25 ~01:00Z (checkpoint: batch 3 merged into work and synced with master; master PR next)
> Older detail: `notes/handoff-history/current-task-2026-11-24a.md` (the previous state, verbatim), plus `-2026-11-20a`, `-16a`, `-15a` and `-13a`.

## Owner mode

- "Continue with open work items that are available." Keep `notes/owner-testing-checklist.md` current.
- The owner works over Remote Control. The closing message says what was done and what failed, plus the decisions needed, as questions with reply choices.

## Quick recovery

- **NOW (2026-11-25 ~02:50Z): task 079 IN PROGRESS, Step 9.5 review running.** Owner chose plan A ("approach with the least impact"). Dev writes DONE (notes/079-wp3-deploy.md, 70a1668fe9; uac-r2 verified): billing-rule-draft Action + one-row Binding; Billing-tab fields on Matter/Project/WA (script a5025d77c6, `-Tables`); sprk_rulereviewpage c60eb53e-76e8-f111-84e8-7ced8d1d8f6b + "Rule Review" sitemap subarea. Source sync DONE and pushed (626a1d1097: 3 forms, Admin sitemap, web resource + RootComponent, PolicyVersion firmdescription/rejectionreason; PendingSchema emptied, ArchTests by filter green; Import-SolutionScoped -PlanOnly exit 0). Temp worktree C:\wt079rr and C:\t079x, C:\t079w removed. Checklist section V added. DEF-069 #3112, ISS-053 #3106 filed. **NEXT (exact):** triage the adversarial-reviewer findings (diff a5025d77c6~1..626a1d1097: forms script, ArchTest, SpaarkeMaster XML); fix F-class; then ask owner for the step 10/11 data go (Procedure Guideline All records + Version 1 + evaluation-set OCG; draft rules, accept 3 edit 1, publish; InvoiceAge maxAgeDays 180 Reject on the same matter; owner submits B-0082 INV-2025-34586 2025-04-01 expecting Rejected InvoiceTooOld; step 11 override on a second matter) and the step 9 signed-in half.
- **NOW (2026-11-25 ~01:00Z): issue batch 3 is merged into work and synced with master (907fff43c6); preparing the master PR.**
  - **Batch 3 on work:** iss049 7affeb4768 (#3057), iss025 6e9054434c (#2876, DEF-055 #2600; re-check PASS), iss015 dbbad24b09 (#2743, #2877), iss051 be99e31311 (#3080), iss052 45e2db2bb8 (#3082; uac-r2 PASS on the route, "include the route in the master PR's authorization note"). `.claude/constraints/jobs.md` updated (db3cee05b0). Master 3067fb3bdc merged in at 907fff43c6; BFF build 0/0.
  - **Master PR #3084 OPEN** (work 20c63201b2; combined filtered tests 2,249/0; publish 41.563 vs master 41.555 MB, +0.008, 194 files each; no vulnerable packages). Waiting for the owner's merge go. spa-r4 copied (no conflict); tell spa-r4 when it merges.
  - **After the PR merges:** iss051 backfill `-Apply` + re-index sweep need the owner's typed OK, and run only after a dev BFF carrying iss051 is live (checklist T). ISS-052 needs the next BFF + external SPA deploy (checklist U; copy spa-r4 on the SPA change).
  - **Invoice Review page on dev** since 22:48Z (see below); owner check = checklist S. uac-r2 is preparing a dev BFF candidate from master 3067fb3bdc (has #3073), owner names the SHA there.
  - **Open, not started:** 079 live steps (owner OK); the 095d parent stays 🔄 (095d-3 Set firm UI is in the Office add-in InvoiceView pane, unverified).
  - **Agent worktrees to remove later** (Remove-WorktreeSafe, all pushed): agent-a38ba0b796285b0ce (iss049), agent-a81991fa0126f70aa (iss025), agent-ab8be5f18e499ec7d (iss015), agent-af1fc20670038b2c3 (iss051), agent-ad31105130a993f0c (iss052), plus the older locked three below.
- **Dev BFF 3067fb3bdc** live since 2026-11-25 00:09:50Z (uac-r2's deploy, owner's go): carries #3073 (ISS-050 lock renewal, ISS-041). Read-only checks ~01:30Z: healthz 200; unauthenticated lines/decisions and approve 401. Batch 3 (#3084) is NOT on dev. Dev SPA is 3067fb3bdc (spa-r4 run 45506591396); spa-r4 asks to be told when #3084 merges, before its next SPA go.
- **Dev BFF 20129e985c** live since 2026-11-24 21:36Z (uac-r2's deploy, owner's go). It carries #3038, #3051 and #3065 (ISS-029, -042, -043/035), but not #3073 (ISS-050). Read-only checks at about 21:45Z: `/healthz` 200; unauthenticated `POST .../invoice-review/confirm` and `.../lines/decisions` both 401. The ISS-029 and ISS-050 behaviour shows only in the Invoice Review page (task 112). The firm labels (ISS-042) show in the portal after the external SPA deploy (checklist O).
- **Invoice Review page deployed to dev 2026-11-24 22:48Z** (owner go "Yes, deploy it" in this session; uac-r2 BEFORE/AFTER sent, its read-back matches): `sprk_invoicereviewpage` id d1f2a11a-59e8-f111-84ea-7ced8dda31f8, from master fa96792811, sha256 08b373e03de91a0738b0e2b3193b1b188f31484b4455d197899d409ccaae43ee. Default solution only: task 112 must still AddSolutionComponent (type 61, SpaarkeMaster). Owner checks it via checklist S.
- **Master PR #3073 MERGED** 2026-11-24 21:32Z at fa96792811 (owner: "Yes, auto-merge"; required checks passed; the advisory Tier 2 api-office shard shows "fail" because its job was cancelled after its tests ran: verdict artifact = pass, 583/0). #3059, #2930, #3042 closed. Work fast-forwarded to master. Batch 2:
  - iss050 (#3059): lock renewal; merged 7da8f29320 after an Opus review, fix rounds 1-2 and a Sonnet re-check.
  - iss041 (#2930): finish-decision notice after a reload; merged efab4736b6 after a Sonnet review, fix round 1 and a re-check PASS.
  - iss047 (#3042): drift-check id grammar; merged 219c0c6207 after a Sonnet review and fix round 1.
  - Work tip: build 0 warnings; filtered InvoiceLineDecision|InvoiceApproval tests 439/0; no overlap with open PRs.
- **Index after the fixed drift check:**
  - 0 status drifts. The 8 POMLs it exposed (084h-1, 095c-5, 130-0, 130-0b, 130e-1, 151c-1a/1b/2) were set to completed; each merge sha was verified as an ancestor.
  - 27 unpaired entries remain: 25 index sub-rows without a POML, plus 095d-1 and 095d-2. The POMLs say not-started, but the 095d row says 1a/1b/1c and 2b-* are merged. Resolve them from evidence.
- **Agent worktrees locked by the app (pid 39572):** agent-a31ddd127a439f9dd (iss041), agent-a73718461abbfd6a5, agent-afe7e9fefac5f933f. All are pushed. Remove them with Remove-WorktreeSafe once unlocked.
- **Master PR #3065 MERGED** 2026-11-24 16:53Z at 6d167e0646 (owner: "Yes, auto-merge"): iss027 (#2878), iss042 (#2933), iss043 (#2978 + #2889), iss029 (#2880), all closed. Found in passing and filed: ISS-049 #3057, ISS-050 #3059. Label wording "Line date" / "Description" kept (owner, EB-D-268).
- **Master PR #3051 MERGED** 2026-11-24 15:14Z at cf1b7027ce (owner: "Yes, merge #3051"): iss048, iss046 (#3001), the rule-type options script fix. iss048 reaches the portal only with the next external SPA build from master (checklist Q's last item).
- **Master PR #3038** (050c + notes) MERGED 2026-11-24 04:16Z at a1ab27702c.
- **Dev BFF 0362599b94** live since 2026-11-24 (uac-r2's deploy; carries 050c). healthz 200; confirm and reject return 401 unauthenticated. Evidence: `notes/050c-confirm-followups.md`. Owner checklist **R** (Office add-in) is ready to test.
- **Checklist Q first run (2026-11-24) Failed** on MAT-000113: the matter is root-BU owned on dev, so the submission was refused `record_owner_restricted_content_team_unresolved`. Not an eBilling defect; uac-r2 confirmed and filed the gap as #3047 (149 root-owned dev records; re-homing is an owner decision uac-r2 raises). Private detail stays with uac-r2. **Re-run:** PAT-792345, invoice number `2026024892`, PO blank (other BU1 matters are listed in checklist Q). Update 2026-11-24 ~16:10Z (uac-r2): the owner approved re-homing the root-owned dev records and it is done, so MAT-000113 can be used again; a parentless record created by the root-homed admin still lands in the root until #3047 is fixed. Resubmitting the dead-lettered MAT-000113 invoice is a live action that needs the owner's go here.
- **iss048 ✅** merged into work f4f5b06f52 (result screen title and icon follow the outcome; record-setup Failed text). Sonnet review: no F-class, K1-K8 in `notes/iss048-submission-result-screen.md`.
- **053:** every read-only check passes, and its BFF and SPA steps are superseded by the master deploys. It closes on owner checklist **Q**: one Accepted firm-portal submission with sample B-0050. The Rejected check moved into **079 step 10**, because dev has no published Reject rule: publish an InvoiceAge rule (maxAgeDays 180, Reject), then the owner submits B-0082 (dated 2025-04-01). Both PDFs were sent to the owner; the copies are in the session scratchpad `samples\`, and the originals in `notes/evaluation-set/local/synthetic/` (gitignored).
- **079 prep (read-only, 2026-11-24), step 2:**
  - These scripts report nothing to create: Procedure schema, Stored File schema, AddedFee/Discount options, rejection-reason schema.
  - Deploy-EbillingRuleTypeOptions.ps1 fixed (d566d01136): it tolerates AddedFee/Discount; -WhatIf exits 0.
  - Step 3: settings, the three containers and the container-scope role are already on dev; live step (b) has nothing to set.
  - Step 4: both -Verify PASS. Evidence: `notes/079-wp3-deploy.md`.
- **ISS-046 (#3001) ✅** merged into work 234828cfb2 (Sonnet review, fix round 1, Pester 27/0). #3001 closes when #3051 merges.
- **ISS-047 (#3042) filed:** the drift-check script misreads `iss028`/`iss040` as 028/040 and does not parse hyphen-suffixed ids. The real drifts were reconciled by hand (084d, 093, 113 completed; 053 and 161 set to 🔄). Two phantom drifts remain (028, 040).
- **Decisions:** EB-D-267 (owner): no typed (no-PDF) invoice create is needed.
- **Blocked chain:** 053 (owner Q) → 079 → 112, 189 and 160. 121 waits for uac-r2 191. 136 and 153 are deploy close-outs after 079/112.
- **Stays off:**
  - 117's Apply stays off until 079, with the owner's OK. #2982 is fixed on master.
  - `Ebilling:StoredFileReconcileEnabled` stays false.
- **Clean-up:** the iss048 and batch agent worktrees are removed, except `agent-a73718461abbfd6a5` (iss029 fix round, branch pushed), locked by the app like the one below. The agent worktree `C:\code_files\spaarke\.claude\worktrees\agent-afe7e9fefac5f933f` is locked by the app (pid 39572). Remove it with Remove-WorktreeSafe once the lock is gone. Its branch was fully pushed.
- **Machine load:** rerun timed-out classes with `-- xUnit.MaxParallelThreads=4`.

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
