# Batch 4 integration steps

This is the running list of obligations the main session takes on while integrating batch 4 and the route-sweep tasks. Each lane's note describes its own step in full; this file only makes sure none is missed. Tick an item when it is done and cite the commit.

## Code reconciliation (on the integration branch)

- [x] **146-c1 hygiene.** The branch contains WIP commit `6cd0c3f28`, which the main session wrote while two agents had collided in the 146 worktree. The coordination files `NOTE-FROM-MAIN.md` and `COORDINATION-FROM-MAIN.md` must NOT be in the merged tree: check with `git show --stat`. **Done 2026-10-04:** no coordination file is tracked in the integration tree.
- [x] **Schema-script verify defect.** The solution-membership check must count a component as included when its parent table is in the solution with `rootcomponentbehavior = 0`. Fix it in every schema script that does the check (`Set-RecordCreatorPersonSchema.ps1`, `Set-NoAccessSystemUserSubjectSchema.ps1`, 150's FLS script, the others: use the Grep tool for `solutioncomponents?`), then re-run 133's and 143's `-Verify` until both PASS (`notes/batch4-live-gates-2026-10-03.md`). **Done** in `0ff4992ed` + `f9b749538`. One helper, `scripts/common/DataverseSolutionMembership.ps1`: it pages and counts subcomponents (`rootcomponentbehavior` 0). All six schema scripts use it, and the 142 ledger script now verifies its solution membership. A comma-joined `-BffApplicationIds` is split. `SchemaScriptSolutionMembershipGuardTests` covers every verify-mode script that touches a solution. Live read-only `-Verify` on dev: 133 PASS, 143 PASS, identity binding PASS, numbering PASS; the child-table creator and 142 ledger await their `-Apply`. **At their merges**, convert 150 `Set-SecureFlagFieldSecurity.ps1`, 166 `Set-DocumentPointerFieldSecurity.ps1` and 168 `Add-AnalysisRegardingRecordUrlColumn.ps1` to the helper (the guard fails until then).
- [ ] **One F3 check.** Task 146-c1 adds a shared F3 helper. Apply 146's recorded replacement so that 150's `UnsecureProjectEndpoint.RefuseUnlessPermittedToRemoveAsync` calls it. After integration, exactly one F3 check exists.
- [x] **The creator column constant.** 146's helper reads `sprk_createdbyperson` by its logical name. Switch it to 133's constant. **Done:** `RecordCreatorPerson.Column` is `Spaarke.Dataverse.RecordCreatorPersonColumn.LogicalName` (146 c1-r1).
- [x] **133's interim stamp** in `DataverseCreateRecordHandler` is replaced by 146's create-as-the-app, which puts the stamp in the create payload (133 note §13.8, owner round 7 item 3). **Done** in `d457890f9`.
- [x] **143 × 149.** `SecureShareNoAccessGuard` and the `SyncRootAsync` callers confirmed present after the 149-r4 merge (`67d20b393`).
- [x] **142 × 149.** 142's Assigned-To materializer wired to 149's child-share fan-out (149's note: the second of the two to land does it) — fix `b1443d12a`, 4 tests.
- [x] **146 × 156 × 142 × 133** hand-merge done in `d457890f9`: update writers = write → 156 restamp → 142 materializer; ONE creator stamp (146's in-payload; 133's interim stamp and 149's `WithCreatorPersonAsync` removed); `ForChild` ignores `sprk_regardingrecordtype`; `EventColumnsWritten` includes the regarding lookups.
- [ ] **Run the external-grid jest suite** (`DataGrid.externalHost.test.tsx`, needs `npm install --legacy-peer-deps --no-audit --no-fund` in `src/client/shared/Spaarke.UI.Components`) and check whether `Spe.Integration.Tests`' 5 build warnings predate the merges.
- [ ] **156 × 146 hand-merge points:** the `DataverseUpdateRecordHandler` constructor, remarks and PATCH block; the `TaskActionCore` / `ActionSeam` / `CreateTaskNodeExecutor` constructors; and `RecordOwnershipContext.ForChild` must ignore `sprk_regardingrecordtype`. 156's note lists them.
- [x] **148 merge** (`task/uac-r2-148-r2`): `UnsecureProjectEndpoint` keeps 132's eviction and 148's Step 3.5. Steps 3.5 and 4 now share ONE `finally` eviction, so a `children_incomplete` return after the record's owner moved also evicts it. Pinned by `SecureChildTransitionTests.Unsecure_WhenTheChildPassIsIncomplete_StillEvictsTheRecordsOwnerChange`. In `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`, I-1 is HEAD's (156) row and I-2 is 148's, plus HEAD's 146 c1/c1-r1 text, the A-UAC146 amendment wording and "G146-1 applied in dev 2026-10-03".
- [ ] **148 × 132 child evictions (do after 132-f1 merges).** 148's `SecureChildReconciler` re-owns children and removes or mirrors their shares in provisioning Step 8, unsecure Step 3.5, the unsecure-completion branch and `SecureChildReconciliationJob`. None of those calls `IMembershipCacheInvalidator`. Every child owner change must call `InvalidateRecordOwnerChangeAsync`, and every child share change must call the share-change eviction 132-f1 adds. Use `CancellationToken.None`; an eviction never fails the pass. Add a test per path, then seed one to prove the test fails without its eviction.
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
- [ ] **146 (round 13 item 9) — HARD PREREQUISITE before the BFF deploy:** apply 146's child-table `sprk_createdbyperson` schema script (dry run → `-Apply` → `-Verify`). 146 now stamps every app-created child unconditionally, so a missing column fails the create (gate G146-6).

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
