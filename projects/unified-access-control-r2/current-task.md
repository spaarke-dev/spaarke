# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md` (round 127 is the latest: Workforce invite mode = option A; census accepted exceptions).
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146, §13 = slice D Restrict, §14 = ebilling EB-D-148 Procedures).
> - **Task 191 design:** `notes/2179-flagged-external-dataverse-design.md` (last section: D-185 input).
> - **Last deploy + gate:** `notes/deploy-gate-2026-10-27-3ff2c04bf5.md`.
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-27 (checkpoint #67).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 wrap-up rules. Merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green. One background merge job at a time. Merge with `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. Public repo: no live exploit mechanism in issues, PRs, commits or committed notes. |
| **Agents** | Opus for executors and verifiers, with every model stated. Helper scripts get a unique prefix in a private folder. Cap 4 concurrent. |
| **Dev live** | BFF `1.0.0+3ff2c04bf5` since **2026-10-27 01:29Z** (R3, owner go). Gate PASS (UAC + R3). Rollback: R3 `deploy/rollback/wwwroot-20261027T012731Z` (bc99fc3e6b). Content adds #2322, #2323, #2324. Census child lines stay Critical (design §5.2 BU restructure, UAT env config). |
| **Peers** | R3 **`spaarke-wt-spa-external-access-platform-08`** (renamed after a restart), ebilling `spaarke-wt-ebilling-r1-5b`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4`, field-mapping `spaarke-wt-field-mapping-server-write-pa-be`, provisioning `spaarke-wt-customer-provisioning-orchest-ff`, ontology `spaarke-wt-spaarke-ontology-platform-r1-79`. |
| **Gate tooling** | App Insights `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`), role `spaarke-bff-dev`. Pass `--offset Nd` (default look-back is 1 h). Use `contains`, not `has_any`, for exception names. Script: temp `uacmerge/uacg/uacg_gate.sh`. |

## Next actions (mine)
1. **Owner round 128 (2026-10-27):** #2337 no cascade on close (not scheduled). **Task 194 (#2339, children follow a reassigned ordinary parent): MERGED** as de38296499 (PR #2349). Role `prvReadsprk_BudgetBucket` applied on dev (owner go; -Verify PASS 33/33). Deploy request sent to R3 (owner go, 2026-10-27); waiting for its build sha + read-back. Next: server gate + read the report-only `ordinaryMoves` report → owner go for `OrdinaryMovesWritesEnabled` → live check (reassign a matter, children + files follow) → close 194. Field-mapping and ebilling were sent the merge head. Remove C:/wt194 after the gate (check junctions). CI flake filed as #2350. Filed with it: #2341 (grant rows don't follow), #2343 (retire legacy sprk_securitybu; owner go for the schema delete), #2348 (sprk_invoicelineitem joins the lineage when ebilling deploys it). **Task 193 (#2338, one add-person entry): pending; starts after R3's combined GHSA merge (same files).** Guide PR #2330 merged (c32a0e3fe0); C:/wtuacdoc removed. Field-mapping 4a-4d recommendations sent (4a: classify the sync modes, no blanket re-push; 4b: filing keeps AppendTo; 4d: no per-BU index, the trim is the boundary). Filed #2329, #2337-#2339.
2. **Census triage sent (2026-10-26):** R3 (keep Critical), ontology (no role change for Console User / Ontology Admin; §5.2 is the fix), ebilling (EB-D-151 unchanged; #1911's four tables will flag the same way).
3. **R3 GHSA-hq7m fix (private), branch fix/r3-iss-005-combined:** slices A, B, B2, B3, C1, C2, C2b, C2c and C3 ACCEPTED. Owner decisions (relayed by R3) and my conditions are in R3's brief; nothing here names the mechanism. C4 (sweep) + C2d delivered at f2e2fefe91. Step (h) of -Verify: System Administrator is exempt with an INFO line (that pinned profile only; any other write-granting profile FAILs; team-only SGA FAILs; 3 tests; a rule-level hardening issue for BFF least privilege). Follow-up commit pending. **2026-10-27: two Opus security passes on 7f867de505..f2e2fefe91 found no critical or high; the combined list was sent to R3 privately: 13 fix-now (record-less grant rows; audit on cancel; modal WorkAccountInvited vs emailSent; retract after a CIAM fault; sweep cap before decide; sweep marker If-Match; reinstate refuses while active grants remain; reinstate reads one row + If-Match; bind re-read checks the marker; registration 403 for removed; validation before the CIAM branch; same-email check on ProvisionExisting; refusal title) + K1 (report-only first run).** Next: review the fix diff + its callers and callees + step (h) → UAC OK → combined merge → deploy via R3 (owner go) → server gate.
4. **ebilling #1911:** EB-D-150/151/152 decided by the owner; review the resulting code and role diff.
5. **#2292** (membership junction never populated): filed; fix when there is capacity.

## Peer reviews owed (UAC is the gate)
- **ebilling:**
  - **090 round 2: ACCEPTED** (12329abebe..64e39a9d5f, sent 2026-10-23). Known limits:
    - K1, a merge obligation: when #2277 is merged in, add `SecuredOnlyByDeployScript` entries, or set Entity.xml `IsSecured=1`, for sprk_ruleset, sprk_procedureassignment and 081's four tables;
    - K2, to be filed: a reused blob keeps its ETag;
    - K3, to be filed: the parent-gone pass does not check `pathParent`.
  - 081 accepted (round 3). The 188 rewrite was accepted. The merge e371dce6bf was reviewed.
  - Still owed: 086 (2 passes), 089, 094, 095, 098 (2 passes), 117.
  - 091 still owes the what-if, `Test-EbillingFileStoreRole.ps1` run live, and the tag-write proof output.
  - Standing rule: no role privilege on the Procedure tables without UAC review. D-185: the External Collaborator role gets none on rule set, policy or policy version.
- **field-mapping:** 017 is blocked; 018b is owed.
- **word-add-in task 130:** two security passes when the PR arrives.

## Owner items outstanding
1. **Task 191:** F1 and S4 block T191b onward. F1 is the flagged user lowering `sprk_accesspermission`; S4 is view/download only.
2. **Task 190:** Apply, and the Restricted call on the 91 unmatched archives plus their 20 children.
3. **Decisions:**
   - #2245 (field-secure `sprk_driveitemid`, or bind the trim to the bound id);
   - #2250 (refuse `sprk_assigned*` on the app-only playbook writers);
   - #2260;
   - K5 / #2270 (via R3);
   - #2262 (the other writing nodes run as the person).
4. **Optional live checks:** partner document read (D7); #2177 Outlook; #2161 re-file; T191 MDA/Copilot/grid checks.
5. **Carried:**
   - #2163 environment-change go.
   - 186: Tracy intake checks 3/5b/6 and the Workforce role export.
   - 036 gates (ii)–(vi).
   - #2153 sign-off (#2152, C:/wt2152, draft).

## Done since checkpoint #60
- **Round 122 (#2234), #2237, #2240, #2246, #2247:** deployed in 1283c41817 (2026-10-22 12:50Z); gate PASS.
- **#2251 (#2253), #1830 / task 192 (#2268), #2257 (#2258), #2273 (#2277):** merged and deployed in 1fa0cf5cb8; gate PASS; task 192 closed.
- **#1830, owner round 125:** dead container removed from the stamp config; 65a3fab2 re-provisioned (new container held by 1 project).
- **Filed:** #2292. **Recorded:** D-185 input for task 191.
- **Housekeeping:** removed worktrees C:/wt2257, C:/wt2273 and C:/wt2246 (their junctions were unlinked first).
- **Next PCF CommunicationActions version:** 1.3.22.
