# Batch 4 integration steps

This is the running list of obligations the main session takes on while integrating batch 4 and the route-sweep tasks. Each lane's note describes its own step in full; this file only makes sure none is missed. Tick an item when it is done and cite the commit.

## Code reconciliation (on the integration branch)

- [ ] **146-c1 hygiene.** The branch contains WIP commit `6cd0c3f28`, which the main session wrote while two agents had collided in the 146 worktree. The coordination files `NOTE-FROM-MAIN.md` and `COORDINATION-FROM-MAIN.md` must NOT be in the merged tree: check with `git show --stat`.
- [ ] **Schema-script verify defect.** The solution-membership check must count a component as included when its parent table is in the solution with `rootcomponentbehavior = 0`. Fix it in every schema script that does the check (`Set-RecordCreatorPersonSchema.ps1`, `Set-NoAccessSystemUserSubjectSchema.ps1`, 150's FLS script, the others: use the Grep tool for `solutioncomponents?`), then re-run 133's and 143's `-Verify` until both PASS (`notes/batch4-live-gates-2026-10-03.md`).
- [ ] **One F3 check.** Task 146-c1 adds a shared F3 helper. Apply 146's recorded replacement so that 150's `UnsecureProjectEndpoint.RefuseUnlessPermittedToRemoveAsync` calls it. After integration, exactly one F3 check exists.
- [ ] **The creator column constant.** 146's helper reads `sprk_createdbyperson` by its logical name. Switch it to 133's constant.
- [ ] **133's interim stamp** in `DataverseCreateRecordHandler` is replaced by 146's create-as-the-app, which puts the stamp in the create payload (133 note §13.8, owner round 7 item 3).
- [ ] **143 × 149.** 149-r3 merged 143 and wired `SecureShareNoAccessGuard` and the `SyncRootAsync` call. Confirm both are present after the integration merge.
- [ ] **156 × 146 hand-merge points:** the `DataverseUpdateRecordHandler` constructor, remarks and PATCH block; the `TaskActionCore` / `ActionSeam` / `CreateTaskNodeExecutor` constructors; and `RecordOwnershipContext.ForChild` must ignore `sprk_regardingrecordtype`. 156's note lists them.
- [ ] **167's ledger** (sweep integration): fill `ResolvedBy` and `ProofTest` from each fix task's "Route authorization ledger input" table, delete the Pending waivers that are now stale, and assign each UNOWNED-NEW entry to an owning task.

## `.claude/` edits (main session only; sub-agents cannot write there)

- [ ] **142:** the concise `.claude/adr/ADR-034-user-record-membership.md` Amendment A4 (ACCEPTED in owner round 11). The exact text is in 142's note.
- [ ] **166:** `.claude/skills/bff-deploy/SKILL.md` §9c. Move its smoke check off `GET /healthz/dataverse/doc/{id}`, which was an anonymous document read, onto `/healthz/dataverse`. The exact text is in 166's note. Also update the runbooks it lists (`projects/dotnet-10-upgrade-r1/notes/slot-swap-runbook.md`, `051-operator-runbook.md`).
- [ ] **160 (only if a production caller needs the SDK path):** a one-line ADR-028 A5 note that an SDK CallerId path satisfies A5's "equivalent refusal".
- [ ] **`.claude/CHANGELOG.md`:** add an entry for each `.claude` edit above.

## Live steps on dev (owner round 11: approved; run each as dry run, then apply, then verify, and record it in the task's live-gate note)

**Before deploying the integrated BFF:**
- [x] **133:** `scripts/Set-RecordCreatorPersonSchema.ps1` APPLIED 2026-10-03 (`92d5f3cc1`). `-Verify` must be re-run to PASS after the schema-script verify fix above.
- [x] **143 G-1:** APPLIED 2026-10-03 (`92d5f3cc1`). Re-run `-Verify` after the script fix.
- [ ] **143 O2** (only an access-administrator role reads `sprk_noaccessentry`): WITH the deploy, because it changes current behaviour.
- [x] **150 G-0:** null repair PASS 2026-10-03 (42 rows). Fix the after-check counting slip at integration.
- [ ] **150 FLS lock** on `sprk_issecure` (invoice included, round 10 item 11): WITH the 150 client and BFF deploy.
- [x] **146 G146-1** (9 → 26, the §5.4 strip, negative and positive probes) and **G146-2**: DONE 2026-10-03.
- [ ] **Copy G146-1's verbatim refusals** from `notes/batch4-live-gates-2026-10-03.md` into the 17 `evidence` fields of `config/secure-record-owner-role.json` on the integration branch, replacing "VERBATIM REFUSAL PENDING".
- [ ] **142:** the ledger schema (`sprk_assignedaccess`) and the "Update Access" ribbon.

**Then:**
- [ ] Deploy the BFF from a fresh worktree.
- [ ] Deploy the external SPA (157), then the BFF again, in 157's documented order.

**After the deploy:**
- [ ] Run each task's manual live gate, using `uac.child.user@demo.spaarke.com` where it needs a child-BU user.
- [ ] Ship gate (owner round 11 item 3): no record is unsecured in a shared environment until 148 is deployed.

## PR text obligations

- [ ] 143's §6.5 path A exception (`IScheduledJobLease` as a per-record mutex; design.md §9).
- [ ] 142's ADR-034 A4 path-B block.
- [ ] 146 and 149 ship together; the 152 ordering.
- [ ] Publish size (fresh short-path worktrees, Compress-Archive, equal file counts) and the CVE check.
- [ ] Each route the sweep tasks deleted, with its no-caller and not-published evidence (owner round 10 item 1).
