# Batch 4 integration steps

This is the running list of obligations the main session takes on while integrating batch 4 and the route-sweep tasks. Each lane's note describes its own step in full; this file only makes sure none is missed. Tick an item when it is done and cite the commit.

## Code reconciliation (on the integration branch)

- [ ] **146-c1 hygiene.** The branch contains WIP commit `6cd0c3f28`, which the main session wrote while two agents had collided in the 146 worktree. The coordination files `NOTE-FROM-MAIN.md` and `COORDINATION-FROM-MAIN.md` must NOT be in the merged tree: check with `git show --stat`.
- [ ] **Schema-script verify defect.** The solution-membership check must count a component as included when its parent table is in the solution with `rootcomponentbehavior = 0`. Fix it in every schema script that does the check (`Set-RecordCreatorPersonSchema.ps1`, `Set-NoAccessSystemUserSubjectSchema.ps1`, 150's FLS script, the others: use the Grep tool for `solutioncomponents?`), then re-run 133's and 143's `-Verify` until both PASS (`notes/batch4-live-gates-2026-10-03.md`).
- [ ] **One F3 check.** Task 146-c1 adds a shared F3 helper. Apply 146's recorded replacement so that 150's `UnsecureProjectEndpoint.RefuseUnlessPermittedToRemoveAsync` calls it. After integration, exactly one F3 check exists.
- [ ] **The creator column constant.** 146's helper reads `sprk_createdbyperson` by its logical name. Switch it to 133's constant.
- [ ] **133's interim stamp** in `DataverseCreateRecordHandler` is replaced by 146's create-as-the-app, which puts the stamp in the create payload (133 note §13.8, owner round 7 item 3).
- [x] **143 × 149.** `SecureShareNoAccessGuard` and the `SyncRootAsync` callers confirmed present after the 149-r4 merge (`67d20b393`).
- [x] **142 × 149.** 142's Assigned-To materializer wired to 149's child-share fan-out (149's note: the second of the two to land does it) — fix `b1443d12a`, 4 tests.
- [x] **146 × 156 × 142 × 133** hand-merge done in `d457890f9`: update writers = write → 156 restamp → 142 materializer; ONE creator stamp (146's in-payload; 133's interim stamp and 149's `WithCreatorPersonAsync` removed); `ForChild` ignores `sprk_regardingrecordtype`; `EventColumnsWritten` includes the regarding lookups.
- [x] **Run the external-grid jest suite**: 7/7, done at the 150 merge. **`Spe.Integration.Tests` warnings (2026-10-04):** no new warnings. Master has 5× CA2024 and integ has 4, all in `AnalysisEndpointsIntegrationTests.cs` (162's rewrite removed one).
- [ ] **`Spaarke.UI.Components` jest (2026-10-04, compared with master `c2ef1857b`):**
  - 12 tests fail every run on integ. One is integ-only: the `todoScoreMappings` sha256 pin. Master fixed it after our merge-base in `b5b0c0ce0` (#1118), so **merge origin/master into integ before the PR**, then re-run.
  - The other 11 fail on master too, with no UAC commit involved: surfaceLaunchRegistry ×2, buildDynamicWorkspaceConfig (h), configResolution ×2 (CRLF comment strip), RichFilePreview ×2, TimelineComposeBox ×3, ConversationView.forward.
  - Root causes are in `scratchpad/q` and in the main-session report.
  - Filed as #1290 so the owning projects can fix them; not UAC code.
- [ ] **156 × 146 hand-merge points:** the `DataverseUpdateRecordHandler` constructor, remarks and PATCH block; the `TaskActionCore` / `ActionSeam` / `CreateTaskNodeExecutor` constructors; and `RecordOwnershipContext.ForChild` must ignore `sprk_regardingrecordtype`. 156's note lists them.
- [ ] **167's ledger** (sweep integration): fill `ResolvedBy` and `ProofTest` from each fix task's "Route authorization ledger input" table, delete the Pending waivers that are now stale, and assign each UNOWNED-NEW entry to an owning task.

## Route-sweep merges (2026-10-04; order binding: 162 before 164, 164 before 163)

- [x] **159** merged `b50db426a`. PUT/DELETE/cancel/logs deleted (round 10 item 1); 146's create ownership re-applied on 159's gated create; 146's PUT re-file F3 gate and 156's PUT re-stamp went with the PUT route (two absence pins replace their tests, seeded). **Owed at 167 integration:** 159 note §5 — the `Api/Events/EventEndpoints.cs` `GovernedFiles` entry (exact text there), `GET /api/v1/events` as a handler decision, delete the eight Pending waivers.
- [x] **160** merged `58aa328e3`. Census 122 → 120. **Owed at 167 integration:** 160 note §8 (drop any 167 entry/waiver for the two deleted files; census −2 on 167's side).
- [x] **161-r1** merged `dc75c60bb`. `sprk_communication` is a NAME-ONLY catalogue entry (156's TaskActionCore name; out of 161's route allow-list; live-verified). 161's "delete the Assign check once 146 lands" NOT done: under a non-parent regarding the supplied owner still picks the owning BU (reason in the merge message). **Owed at 167 integration:** 161 note §9 ledger rows.
- [x] **162-f1** merged `8fd16bbea`. Fork handler/tests gone; promote = 162's session-document check, then 146's owner. **Owed at 167 integration:** 162 note §7/§8. **Owed at 164's merge:** 162 §14.3 one-line edit (done in the 164 merge, see below).
- [x] **Round 34 item 1** (`scripts/Set-DocumentAnalysisCascadeSchema.ps1`, dry run + `-Verify` read-only on spaarkedev1: FAIL exit 1 as expected pre-apply) — commit below. **Manual gate:** `-Apply` → `-Verify` exit 0 → non-admin delete probe (162 note §14.14).
- [x] **Round 34 item 2** confirmed + missing-row test added, seeded both ways (162 note §14.14).
- [x] **Round 34 commit** `ebb3ba38f` (items 1 and 2 above).
- [x] **164-r1** merged `672923b0b`. Associate/match-records routes retired: 146's seven associate tests + harness, 156's associate re-stamp test, the route guard's GovernedFiles entry + match-records Pending waiver, and the owner-census entry left with the file (dated notes). Read-rule conflict: took 162 f1's superset. **162 §14.3 edit done**: `AiAuthorizationFilter.IsAnalysisReadableAsync` delegates to the shared evaluator; new chat test for the personal-creator host, seeded. Also fixed 156's report-card test (161 live-verified its name column). **Owed at 167 integration:** 164 note §9 ledger rows. **Infra (optional, main session):** retire the Cosmos `prompts` container from bicep (164 note §5).
- [x] **163-f1** merged `99fe4c3f6`. Census 118 → 117. **Owed at 167 integration:** 163 note §15.7 (delete the 17 Pending(163) waivers; the three CreditedForms; the TenantAuthorizationFilter evidence line; AdminOnlyRoutes += `POST /api/ai/rag/index`, `DELETE /api/ai/rag/{documentId}`; SweepFindings ResolvedBy/ProofTest) — round 34 items 4-5 decide 163's two 167 contract gaps.
- [ ] **Pre-existing whitespace debt (advisory CI format job)** on the merged tree, none from the merge edits: `FinanceAuthorizationFilter.cs` 389-410 (`8f0f1b8fc`), `AnalysisEndpoints.cs`/`AnalysisResultPersistence.cs` (146 `738bed883`), and 162 test files (`eaf21d9b9`, `d1c5e6f34`, `f86c3902b`). `dotnet format whitespace` fixes them in one pass if wanted.

## `.claude/` edits (main session only; sub-agents cannot write there)

- [ ] **142:** the concise `.claude/adr/ADR-034-user-record-membership.md` Amendment A4 (ACCEPTED in owner round 11). The exact text is in 142's note.
- [ ] **166:** `.claude/skills/bff-deploy/SKILL.md` §9c. Move its smoke check off `GET /healthz/dataverse/doc/{id}`, which was an anonymous document read, onto `/healthz/dataverse`. The exact text is in 166's note. Also update the runbooks it lists (`projects/dotnet-10-upgrade-r1/notes/slot-swap-runbook.md`, `051-operator-runbook.md`).
- [ ] **160 (only if a production caller needs the SDK path):** a one-line ADR-028 A5 note that an SDK CallerId path satisfies A5's "equivalent refusal".
- [x] **161 (optional pointer):** DONE (sweep .claude commit). `.claude/patterns/api/endpoint-filters.md`, add one line under the filter list: "`CommunicationRecordAuthorizationFilter` — per-record gate for `/api/communications` routes; one `CommunicationRecordRoute` value per route fixes the id source, the right and the deny answer (task 161)."
- [x] **163:** DONE (sweep .claude commit). `.claude/skills/add-reference-to-index/SKILL.md` line 176 — replace
  `` - `src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs` — BFF API indexing service ``
  with
  `- (ReferenceIndexingService and /api/admin/knowledge/* were removed by unified-access-control-r2 task 163 — the scripts above are the only indexing path)`.
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
- [ ] Merge origin/master into integ (picks up #1118's todoScoring pin fix and anything else master gained), rebuild, re-run the suites.
- [ ] Publish size (fresh short-path worktrees, Compress-Archive, equal file counts) and the CVE check.
- [ ] Each route the sweep tasks deleted, with its no-caller and not-published evidence (owner round 10 item 1).
