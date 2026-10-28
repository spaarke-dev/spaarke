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
1. **Task 194b (#2339 fix):** C:/wt194, branch `fix/uac-194b-ownership-primary`, Opus implementer aae454a2d943c5204. 194 is deployed with `OrdinaryMovesWritesEnabled` OFF. Run history is in `notes/task-194-ordinary-parent-move.md`. K issues: #2378-#2381.
   - R5 is done at bd5208d0b5 (all gates green; publish +23 KB). Dev simulation: 16 planned moves, 8/8 spot-checks correct.
   - V-fix 3743e0fabf + item 3 cfa7bbe421 accepted. R4b-2 e34be8fa39 (hard-deny tables → content team) and R4b-2b 129a2225e3 (ontology F1/F3/F4; a re-file keeps the author) are accepted. R4b-3 HELD at b85176fc6e (built for the root BU). **Owner round 136:** the content team lives in the CUSTOMER top BU (the root's direct child), never the root → R4b-2c DONE at **db5880dd32** (team per customer top BU; F2; F3 a-e; 19/19 perturbations red; +35.6 KB) → **ReparentAsync author-keeps fix RUNNING** → security adversarial pass of R4b → ontology re-check. Dev setup PROPOSED (owner go): `Set-RestrictedContentOwnerTeam.ps1 -CustomerBusinessUnitId` = BU1 cb15f587; dev root holds 5 matters, 6 projects, 25 WAs and 172 users. Brief: temp `uacmerge/uacg/r194/r4b2_brief.md`.
   - Next: ontology re-check → owner go for the team and role on dev (customer BU) → send the sha to ebilling (EB-D-168 waits) → PR → merge → deploy via R3 → report-only run + ontology's live memo-LIKE proof → owner go for writes → live check → close. Then remove C:/wt194 (check junctions first).
2. **R3:** #2386 merged as e56a3a51d4, -Verify PASS, deployed to dev ~14:11Z. **Gate PASS** (sent to R3): health OK; report-only, moved=0; the dev FLS-READ proofs pass; the ACME failures are pre-existing (#2163); the caller-stamp check is deferred to #2389. Master BFF to dev is unblocked. Remove C:/wtuacsec when convenient (check junctions first).
3. **Task 193 (#2338, one "Add person"):** C:/wt193, branch `fix/uac-193-add-person`, head **528daa090d** (F-final done; all gates green; TrackingFieldTrio 1.0.57). Merges cleanly with master (79 behind). Runs, owner behaviour table and K list: `notes/task-193-add-person-notes.md`. Remaining demo-registration log sites added to #2134. #2371 and #2377 filed.
   - **Owner round 131:** an internal colleague added either way (user or contact) gets a share AND a grant on their linked contact, with no end date on either; revoking removes both. Run G split: G1 DONE at **6a73134482** (`Deploy-ExternalRecordAccessPairedSystemUser.ps1` not run; R5 cleanup; K: R5 counts direct shares only) → G2 DONE at **a9dd8ce420** (`/unshare-user` ends the paired grant; `pairedWith`) → G2b DONE at **a970a05b33** (No Access, Restricted, inheritance and materializer end the paired grant; `PairedGrantFollowsShareRevokeGuardTests`) → G3a DONE at **c2512a8075** (item 4 not a defect, pinned by tests; one pairing-column Warning per pass) → **G3b RUNNING** (TrackingFieldTrio text, fresh agent) → G3c (guide, notes, publish delta, master merge). The inheritance safety net on a no-longer-secure parent → task 196. Details: `notes/task-193-add-person-notes.md` (on the branch). Then two security verifier passes. The guide text follows the ruling.
   - Then: merge master into the branch → re-gate → publish delta → PR (owner behaviour table; no F1-b access-loss wording until the owner rules) → merge → deploy via R3 → TrackingFieldTrio import via Import-SolutionScoped.ps1 → gate.
4. **Task 195 (#2352 (i)+(ii), owner round 129):** the server writes a project's regarding pair (one root); N:N stays association only (three N:N cases in the POML); backfill 7 dev projects (owner go). Starts after 194b merges. Field-mapping W7/040 depend on 194b + 195 shas.
4b. **Task 196 (rounds 135 + 138): Unsecure keeps all access; child shares follow the record.** Map DONE; design `notes/task-196-unsecure-keeps-access.md`. 196a after 193 merges; 196b after 194b. Filed #2406 (JIT prune), #2407 (team shares hidden).
5. **#2382:** MERGED as PR #2394 (bafe310ca4); dev roles live, -Verify PASS. Follow-ups: #2393, #2395-#2398; #2396 needs a role decision (Report Card create). #2401 closed (round 136: dev layout). Next: ask the owner to retry UAT check 5, then close #2382. Lint-staged stash backups remain (drop by tag later).
6. **ebilling:** EB-D-162 merged into work/ebilling-r1 with EB-D-150 entry removed; live on dev (-Verify PASS). Its master PR carries both integration suites' counts.
7. **#2027:** org audit verified on dev (-1, on, on). ACME read-back and table flags asked of provisioning.
8. **#2292** (membership junction never populated): filed; fix when there is capacity.

## Peer reviews owed (UAC is the gate)
- **ebilling (10-28):** 095 OK to merge into work (#2390). 094 at c7253943f5: NOT READY (F4-1 withdraw before the deletes; K2-b, K4-a, K1-a, sprk_storedfile role guard); `.eml` anchor ACCEPTED. 098 round 4 at 5bbd024111: re-check RUNNING (integration 199/353 OK). Standing: no Procedure-table role privilege without UAC review; D-185.
- **ebilling 086:** superseded by round 131/136: hard-deny ordinary rows → the customer-BU restricted-content team (194b R4b-2c). EB-D-168a waits for the 194b sha. 191: no BU or Deep Read on these tables.
- **field-mapping:** 017 is blocked; 018b is owed. 070 (#2367) and 037 (#2370) merged. After 194b merges master: run RecordFamilyEdgesGuardTests and 037's pins (`SecureRootInheritanceWriterTests.ChildRecordRoute_AWorkAssignmentUnderASecureMatter_…`, `RecordTableCopiedDefaultGuardTests`), and check the copied default still lands before the ownership step in `OwnedChildWrite.CreateAsync`. 071 takes the primary from 194b ordering and starts after 194b merges.
- **word-add-in task 130:** two security passes when the PR arrives.

## Owner items outstanding
1. **Task 191:** F1 and S4 block T191b onward.
2. **Task 190:** Apply, and the Restricted call on the 91 unmatched archives plus their 20 children.
3. **Decisions:** #2343 schema delete; #2245; #2250; #2260; K5 / #2270 (via R3); #2262.
4. **Optional live checks:** partner document read (D7); #2177 Outlook; #2161 re-file; T191 MDA/Copilot/grid checks.
5. **Carried:** #2163 environment-change go; 186 Tracy intake checks 3/5b/6 and the Workforce role export; 036 gates (ii)–(vi); #2153 sign-off (#2152, C:/wt2152, draft).

## Recently filed
#2393, #2395-#2398, #2406, #2407 (this session); #2401 closed.
