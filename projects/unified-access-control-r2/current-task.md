# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md` (round 129 is the latest: #2352 filing root (a); one family per record, D-191).
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146, §13 = slice D Restrict, §14 = ebilling EB-D-148 Procedures).
> - **Task 191 design:** `notes/2179-flagged-external-dataverse-design.md` (last section: D-185 input).
> - **Last deploy + gate:** `notes/deploy-gate-2026-10-27-3ff2c04bf5.md`; task 194 (de38296499) deployed 19:41Z, gate PASS.
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-28 (checkpoint #71).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 wrap-up rules. Merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green. One background merge job at a time. Merge with `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3 (owner's go given directly in R3's session). A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. Public repo: no live exploit mechanism in issues, PRs, commits or committed notes. |
| **Agents** | Opus for executors and verifiers, with every model stated. Helper scripts get a unique prefix in a private folder (temp `uacmerge/uacg/r194/`). Cap 4 concurrent. |
| **Peers** | R3 `spaarke-wt-spa-external-access-platform-08`, ebilling `spaarke-wt-ebilling-r1-5b`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4`, field-mapping `spaarke-wt-field-mapping-server-write-pa-be`, provisioning `spaarke-wt-customer-provisioning-orchest-ff`, ontology `spaarke-wt-spaarke-ontology-platform-r1-79`. |
| **Gate tooling** | App Insights `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`), role `spaarke-bff-dev`. BFF host `spaarke-bff-dev.azurewebsites.net`. Job status: `GET /api/admin/jobs/{jobId}/status` (in-memory store). Script: temp `uacmerge/uacg/uacg_gate.sh`. |

## Next actions (mine)
1. **Task 194b (#2339 fix):** C:/wt194, branch `fix/uac-194b-ownership-primary`, Opus implementer aae454a2d943c5204. 194 is deployed with `OrdinaryMovesWritesEnabled` OFF. Run history is in `notes/task-194-ordinary-parent-move.md`. K issues: #2378-#2381.
   - R5 is done at bd5208d0b5 (all gates green; publish +23 KB). Dev simulation: 16 planned moves, 8/8 spot-checks correct.
   - V-fix 3743e0fabf accepted (items 1, 2, 4-8). Item 3 ruled: the canonical counts for Secure-if-any only, never as the ordinary primary. Owner round 131: the hard-deny tables → a memberless root-BU content-owner team, fail closed; team and role codified, no live apply. **Split:** item 3 RUNNING (implementer; Office option (a): resolve the editable identity before the owner question, no new DI dependency) → R4b-1 read-only writer map (RUNNING; `sprk_storedfile` has no BFF writer on master) → R4b-2 resolver/reconciler (implementer) → R4b-3 definitions/script/guard/docs (implementer). Pattern: `SecureRecordOwnerTeam.IdentifyAsync` + `config/secure-record-owner-role.json` + `Set-SecureRecordOwnerRolePrivileges.ps1`.
   - Next: scoped Opus re-check of V-fix + item 3 + R4b → owner go for the team and role on dev → send the sha to ebilling (086, 092 and 094 persister, 089 writers wait on it) and ontology → PR → merge → deploy via R3 (after #2386 merges and -Verify exits 0) → report-only run → owner go for writes → live check → close. Then remove C:/wt194 (check junctions first).
2. **R3:** #2360 merged (078f62bcad). -Verify (g) FAILs; the fix is **PR #2386**; merged e56a3a51d4; -Verify PASS (both app users OK, not OK-ROLE). **My OK given to deploy e56a3a51d4 to dev** (owner go in R3). Server gate on its deploy notice: health, the report-only reconcile, the #2389 live check, the identity proof reads. Impersonation: the header is honoured live, WhoAmI ignores it; filed #2389 (ArchTest + a post-deploy check through an existing route). Review: fail-closed filter; grep every impersonation path (CallerObjectId/MSCRMCallerID: an ignored header = fail-open); (g) "OK (role leg)" as a distinct state. No master BFF to dev until it merges and -Verify exits 0. Remove C:/wtuacsec after the gate.
3. **Task 193 (#2338, one "Add person"):** C:/wt193, branch `fix/uac-193-add-person`, head **528daa090d** (F-final done; all gates green; TrackingFieldTrio 1.0.57). Merges cleanly with master (79 behind). Runs, owner behaviour table and K list: `notes/task-193-add-person-notes.md`. Remaining demo-registration log sites added to #2134. #2371 and #2377 filed.
   - **Owner round 131:** an internal colleague added either way (user or contact) gets a share AND a grant on their linked contact, with no end date on either; revoking removes both. Merged master at 4befa3d59a (gates green except an ISS-005 timing test under load). Run G split: **G1 RUNNING** (round 133: paired grant normally dated + a cleanup rule in the reconciliation job; new BFF-only column `sprk_pairedsystemuser`; reverse lookup on `ContactUserRepresentation`; only Restricted or unreadable flags give share only + `workforceAppVisible: false`; Secure gives share + grant as today) → G2 (paired revoke by the column; `pairedWith` from the column) → G3 (wording, guide, notes, TrackingFieldTrio, full gates). Then two security verifier passes. The guide text follows the ruling.
   - Then: merge master into the branch → re-gate → publish delta → PR (owner behaviour table; no F1-b access-loss wording until the owner rules) → merge → deploy via R3 → TrackingFieldTrio import via Import-SolutionScoped.ps1 → gate.
4. **Task 195 (#2352 (i)+(ii), owner round 129):** the server writes a project's regarding pair (one root); N:N stays association only (three N:N cases in the POML); backfill 7 dev projects (owner go). Starts after 194b merges. Field-mapping W7/040 depend on 194b + 195 shas.
5. **#2382** (rounds 132 + 134): C:/wt2382. Membership writes → Access Administrator (Core/Basic/Workforce keep Read), then Core User AppendTo at Read depth (contact, organization, account Deep; four `_ref` tables Global). Guards, error helper, dev DRY RUN → my review → -Apply → PR. Reply to ontology after.
6. **ebilling:** EB-D-162 merged into work/ebilling-r1 with EB-D-150 entry removed; live on dev (-Verify PASS). Its master PR carries both integration suites' counts.
7. **#2027:** org audit verified on dev (-1, on, on). ACME read-back and table flags asked of provisioning.
8. **#2292** (membership junction never populated): filed; fix when there is capacity.

## Peer reviews owed (UAC is the gate)
- **ebilling (10-28):** 095 conditions met at 85464826b6; OK to merge into work (#2390 filed). 094 d7ec16cb97 READY (undo order agreed with conditions: the over-MaxFiles hold, the servable-row note, the §9.1 amendment; fold in the shared D-140 hop). 098 ACCEPTED at f2446e638e (channel plus sourceless-InvoicePdf rule). 094 still owes the InvoicePdf same-commit writer guard. 086 ruling sent (EB-D-168); 086, 092 and 089 wait on the R4b sha. Earlier: 089 (full review on the reconciled head, after it merges 194b), 086 (2 passes), 094 (review of 5a5ffabfa8 sent: not ready, F1-a/b/c + F2-a/b; K2 and K5 conditions; re-check the fix head), 095, 098 (2 passes), 117; 091 still owes the what-if, `Test-EbillingFileStoreRole.ps1` live, and the tag-write proof. 090 K1 merge obligation (`SecuredOnlyByDeployScript` / `IsSecured=1` for sprk_ruleset, sprk_procedureassignment and 081's four tables). Standing rule: no role privilege on the Procedure tables without UAC review; D-185: External Collaborator gets none on rule set, policy or policy version.
- **ebilling 086 BLOCKER (ruling mine):** the ordinary owner of hard-deny tables (sprk_storedfile, sprk_procedureassignment) = the BU default team, which lacks Read → assign/create refused. Options (a) app owner while ordinary, Secure team when Secure; (b) per-BU memberless team; (c) block. RULED (a), extended to sprk_ruleset/sprk_policy/sprk_policyversion (#2368). **Awaiting the OWNER's go in MY session** (ebilling told not to double-ask). Once approved, UAC builds the resolver rule + Unsecure move-out in 194b (new run after R3b-2); ebilling's writers adopt ForRow + the app-owner outcome after merging 194b. 191 constraint: no BU/Deep Read on these tables. 098 answered: K for the read_query cap (both sides) and the SemanticSearch (a′) gap → #2369, with 191. One dev role-hygiene finding was told to the owner in chat (not recorded here).
- **field-mapping:** 017 is blocked; 018b is owed. 070 (#2367) and 037 (#2370) merged. After 194b merges master: run RecordFamilyEdgesGuardTests and 037's pins (`SecureRootInheritanceWriterTests.ChildRecordRoute_AWorkAssignmentUnderASecureMatter_…`, `RecordTableCopiedDefaultGuardTests`), and check the copied default still lands before the ownership step in `OwnedChildWrite.CreateAsync`. 071 takes the primary from 194b ordering and starts after 194b merges.
- **word-add-in task 130:** two security passes when the PR arrives.

## Owner items outstanding
1. **Task 191:** F1 and S4 block T191b onward.
2. **Task 190:** Apply, and the Restricted call on the 91 unmatched archives plus their 20 children.
3. **Decisions:** #2343 schema delete; #2245; #2250; #2260; K5 / #2270 (via R3); #2262.
4. **Optional live checks:** partner document read (D7); #2177 Outlook; #2161 re-file; T191 MDA/Copilot/grid checks.
5. **Carried:** #2163 environment-change go; 186 Tracy intake checks 3/5b/6 and the Workforce role export; 036 gates (ii)–(vi); #2153 sign-off (#2152, C:/wt2152, draft).

## Recently filed
#2350 (Classify Tier 1 timeout), #2341, #2343, #2348, #2337-#2339, #2329.
