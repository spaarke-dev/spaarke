# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md` (round 136 is the latest: customer BU = top level for customer records; 135: Unsecure keeps all access).
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146, §13 = slice D Restrict, §14 = ebilling EB-D-148 Procedures).
> - **Task 191 design:** `notes/2179-flagged-external-dataverse-design.md` (last section: D-185 input).
> - **Last deploy + gate:** e56a3a51d4 (R3 #2386) deployed to dev 2026-10-28 ~14:11Z; gate PASS (a, b, d), (c) deferred to #2389. The ACME 0x80072560 proof-read failures and ID-LINK-RECON aborts are pre-existing (#2163). Previous: `notes/deploy-gate-2026-10-27-3ff2c04bf5.md`.
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-28 (checkpoint #71).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 wrap-up rules. Merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green. One background merge job at a time. Merge with `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3 (owner's go given directly in R3's session). A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. Public repo: no live exploit mechanism in issues, PRs, commits or committed notes. |
| **Agents** | Opus for executors and verifiers, with every model stated. Helper scripts get a unique prefix in a private folder (temp `uacmerge/uacg/r194/`). Cap 4 concurrent. |
| **Peers** | R3 `spaarke-wt-spa-external-access-platform-08`, ebilling `spaarke-wt-ebilling-r1-5b`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4`, field-mapping `spaarke-wt-field-mapping-server-write-pa-be`, provisioning `spaarke-wt-customer-provisioning-orchest-2f`, ontology `spaarke-wt-spaarke-ontology-platform-r1-79`. |
| **Gate tooling** | App Insights `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`), role `spaarke-bff-dev`. BFF host `spaarke-bff-dev.azurewebsites.net`. Job status: `GET /api/admin/jobs/{jobId}/status` (in-memory store). Script: temp `uacmerge/uacg/uacg_gate.sh`. |

## Next actions (mine)
1. **Task 194b (#2339 fix):** C:/wt194, branch `fix/uac-194b-ownership-primary`. 194 deployed with `OrdinaryMovesWritesEnabled` OFF. History: `notes/task-194-ordinary-parent-move.md` (on the branch). K issues: #2378-#2381. Brief: temp `uacmerge/uacg/r194/r4b2_brief.md`.
   - Accepted: R5 bd5208d0b5, V-fix 3743e0fabf, cfa7bbe421, R4b-2 e34be8fa39, R4b-2b 129a2225e3. R4b-3 b85176fc6e superseded (round 136).
   - R4b-2c db5880dd32 (content team per customer top BU) → R4b-2d 92e3cc8ed7 (re-file keeps author) → R4b-3b **12aeb8742a** (`Set-CustomerOwnerTeams.ps1` sets up both teams; config role Read Basic on sprk_aisearchindex/fieldmappingprofile/fieldmappingrule; guard 31/31; ArchTests 1629; dev dry run BU1 = 12 steps, re-own 11/7/40 rows) → 2 security passes done (A: reconcile fault crash, team-type pin, report broad-owner rows; B: script Secure-unit fail-open, guard one-way, doc order, seeder claim) → R4b-4 **68ffc79cf0** (all fixed; ArchTests 1637; +620 B; Pester job in ci-standalone-checks) → **fix-diff re-check RUNNING** (Sonnet). 196b input recorded.
   - Next: fixes → ontology re-check → owner go: `Set-CustomerOwnerTeams.ps1` on BU1 cb15f587 + field-mapping re-own → sha to ebilling (EB-D-168a) and field-mapping → PR → merge → deploy via R3 → report-only run + ontology memo-LIKE proof → owner go for writes → close. Remove C:/wt194 (check junctions).
2. **R3:** #2386 merged as e56a3a51d4, -Verify PASS, deployed to dev ~14:11Z. **Gate PASS** (sent to R3): health OK; report-only, moved=0; the dev FLS-READ proofs pass; the ACME failures are pre-existing (#2163); the caller-stamp check is deferred to #2389. Master BFF to dev is unblocked. Remove C:/wtuacsec when convenient (check junctions first).
3. **Task 193 (#2338, one "Add person"):** C:/wt193, branch `fix/uac-193-add-person`. Notes on the branch: `notes/task-193-add-person-notes.md` (the branch copy supersedes the project copy; take it on merge). #2371, #2377 filed.
   - Round 131 (share + paired dated workforce grant; revoke ends both): G1 6a73134482 → G2 a9dd8ce420 → G2b a970a05b33 → G3a c2512a8075 → G3b f6eb5acd3f (modal text; TrackingFieldTrio 1.0.58) → G3c **efb948ace3** (master merged clean; guide + notes; unit 12129/0, Arch 1620, BFF int 186/0/8, Spe 353/0/23; publish +98 KiB) → G3d **7c592a34ee** (no timing promise; TrackingFieldTrio 1.0.59) → G3e **c4ed79080b** (orphaned grant standalone wording; 1.0.60) → **2 security passes RUNNING** (A server, B client).
   - Then: two security passes → PR (owner behaviour table) → merge → owner go for `Deploy-ExternalRecordAccessPairedSystemUser.ps1` → deploy via R3 → TrackingFieldTrio import via Import-SolutionScoped.ps1 → gate. Owner Q (optional): turn R5 writes on?
4. **Task 195 (#2352 (i)+(ii), owner round 129):** the server writes a project's regarding pair (one root); N:N stays association only (three N:N cases in the POML); backfill 7 dev projects (owner go). Starts after 194b merges. Field-mapping W7/040 depend on 194b + 195 shas.
4b. **Task 196 (rounds 135 + 138): Unsecure keeps all access; child shares follow the record.** Map DONE; design `notes/task-196-unsecure-keeps-access.md`. 196a after 193 merges; 196b after 194b. Filed #2406 (JIT prune), #2407 (team shares hidden).
5. **#2382:** MERGED as PR #2394 (bafe310ca4); dev roles live, -Verify PASS. Follow-ups: #2393, #2395-#2398; #2396 needs a role decision (Report Card create). #2401 closed (round 136: dev layout). Next: ask the owner to retry UAT check 5, then close #2382. Lint-staged stash backups remain (drop by tag later).
6. **ebilling:** EB-D-162 merged into work/ebilling-r1 with EB-D-150 entry removed; live on dev (-Verify PASS). Its master PR carries both integration suites' counts.
7. **#2027:** org audit verified on dev (-1, on, on). ACME read-back and table flags asked of provisioning.
7b. **Task 197 (owner 10-28: fix now, no tracking; details chat only):** C:/wt197, branch `fix/uac-197-stored-match-and-session-owner` off a8314c2e0e. Split into runs A-G; A+B DONE at 1e86c8b107 (session owner checks; invoice search trim); parent-session empty-owner fix + 197-C RUNNING, then (stored trace), D (client), E (dev report-only script), F (guard), G (gates). -Apply for the dev clean-up needs the owner's go. Then 2 security passes → PR (neutral text) → merge → deploy via R3. Chat copies of record documents accepted as a K (owner).
8. **#2292** (membership junction never populated): filed; fix when there is capacity.

## Peer reviews owed (UAC is the gate)
- **ebilling (10-28):** 095 OK to merge into work (#2390). 094 at c7253943f5: NOT READY (F4-1 withdraw before the deletes; K2-b, K4-a, K1-a, sprk_storedfile role guard); `.eml` anchor ACCEPTED. 098 round 4 at 5bbd024111: NOT READY (F1 AI tools miss no-invoice submissions; Workforce role has Deep Read on sprk_invoicesubmission live, so reachable; F3 DI wiring). Round 5 running; re-check only its diff. Standing: no Procedure-table role privilege without UAC review; D-185.
- **ebilling 086:** superseded by round 131/136: hard-deny ordinary rows → the customer-BU restricted-content team (194b R4b-2c). EB-D-168a waits for the 194b sha. 191: no BU or Deep Read on these tables.
- **field-mapping:** 017 is blocked; 018b is owed. 070 (#2367) and 037 (#2370) merged. After 194b merges master: run RecordFamilyEdgesGuardTests and 037's pins (`SecureRootInheritanceWriterTests.ChildRecordRoute_AWorkAssignmentUnderASecureMatter_…`, `RecordTableCopiedDefaultGuardTests`), and check the copied default still lands before the ownership step in `OwnedChildWrite.CreateAsync`. 071 takes the primary from 194b ordering and starts after 194b merges.
- **field-mapping design §13 (#2399):** ACCEPTED at 86dc5c1f92 (FR-38, FR-39; #2413 known limit). §13.5 Q2 (index rows per customer) is field-mapping's to raise. AI trim inventory done.
- **word-add-in task 130:** two security passes when the PR arrives.
- **ontology #2418** (task 168 Assign Task email + bell) at 8064311c54: NOT READY (F-1 no recipient access gate; F-2 fallback reach; F-3 job re-check; F-4 inactive contact). Owner Qs via ontology. K-1: conflicts with 193 `ContactUserRepresentation`. #2422 (D-198) APPROVED. Filed #2423 (send log names the sender address).
- **ontology:** #2409 merge OK; #2414 (d as caller) APPROVED at 48340e1ed3. D-198 (secure matter currency = `ExternalAccess:CustomerBusinessUnitId`) rule confirmed with 5 tests asked.

## Owner items outstanding
1. **Task 191:** F1 and S4 block T191b onward.
2. **Task 190:** Apply, and the Restricted call on the 91 unmatched archives plus their 20 children.
3. **Decisions:** #2343 schema delete; #2245; #2250; #2260; K5 / #2270 (via R3); #2262.
4. **Optional live checks:** partner document read (D7); #2177 Outlook; #2161 re-file; T191 MDA/Copilot/grid checks.
5. **Carried:** #2163 environment-change go; 186 intake checks 3/5b/6 and the Workforce role export; 036 gates (ii)–(vi); #2153 sign-off (#2152, C:/wt2152, draft).

## Recently filed
#2393, #2395-#2398, #2406, #2407 (this session); #2401 closed.
