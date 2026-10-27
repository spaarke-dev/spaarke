# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md` (round 127 is the latest: Workforce invite mode = option A; census accepted exceptions).
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146, §13 = slice D Restrict, §14 = ebilling EB-D-148 Procedures).
> - **Task 191 design:** `notes/2179-flagged-external-dataverse-design.md` (last section: D-185 input).
> - **Last deploy + gate:** `notes/deploy-gate-2026-10-27-3ff2c04bf5.md`.
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-26 (checkpoint #63).

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
1. **#2322, #2323, #2324 deployed in 3ff2c04bf5; gate PASS; #2320 closed.** Filed #2325 (to-do Rule 2 matter column; not UAC). Nothing of mine is queued.
2. **Census triage sent (2026-10-26):** R3 (keep Critical), ontology (no role change for Console User / Ontology Admin; §5.2 is the fix), ebilling (EB-D-151 unchanged; #1911's four tables will flag the same way).
3. **R3 GHSA-hq7m fix (private), branch fix/r3-iss-005-combined:** re-check slice-B review fixes; follow B2 (option A: server-side tenant resolve, workforce-any, CIAM token against workforce-any → deny); then slice C; two security passes on the combined diff before merge; ISS-005 (c05d1b2788) with or after. Slice-B review fixes (907e567c69..ff1ade8380) and the FLS answer ACCEPTED. B2 (ff1ade8380..97ba1614e7) ACCEPTED 2026-10-26, no F; K1 optional (IOException/InvalidDataException on body read → 500 not 503; chunked body unbounded). Owner decided in R3's session (relayed by R3, 2026-10-26): auto-invites go through the resolver (grant reports not-sent, materializer defers; no portal fallback); work-account notice yes; NO staff send-as-portal override (removed; supplied value 400; fault = 503 retry only). B3 (running) adds those + K1 + CIAM relax on work-account branch + modal retry UI; my 6 B3 checks sent. Next: slice C, then two security passes on the combined diff.
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
