# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md` (round 129 is the latest: #2352 filing root (a); one family per record, D-191).
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146, §13 = slice D Restrict, §14 = ebilling EB-D-148 Procedures).
> - **Task 191 design:** `notes/2179-flagged-external-dataverse-design.md` (last section: D-185 input).
> - **Last deploy + gate:** `notes/deploy-gate-2026-10-27-3ff2c04bf5.md`; task 194 (de38296499) deployed 19:41Z, gate PASS.
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-27 (checkpoint #70).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 wrap-up rules. Merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green. One background merge job at a time. Merge with `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3 (owner's go given directly in R3's session). A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. Public repo: no live exploit mechanism in issues, PRs, commits or committed notes. |
| **Agents** | Opus for executors and verifiers, with every model stated. Helper scripts get a unique prefix in a private folder (temp `uacmerge/uacg/r194/`). Cap 4 concurrent. |
| **Peers** | R3 `spaarke-wt-spa-external-access-platform-08`, ebilling `spaarke-wt-ebilling-r1-5b`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4`, field-mapping `spaarke-wt-field-mapping-server-write-pa-be`, provisioning `spaarke-wt-customer-provisioning-orchest-ff`, ontology `spaarke-wt-spaarke-ontology-platform-r1-79`. |
| **Gate tooling** | App Insights `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`), role `spaarke-bff-dev`. BFF host `spaarke-bff-dev.azurewebsites.net`. Job status: `GET /api/admin/jobs/{jobId}/status` (in-memory store). Script: temp `uacmerge/uacg/uacg_gate.sh`. |

## Next actions (mine)
1. **Task 194b (#2339 fix) — branch `fix/uac-194b-ownership-primary` in C:/wt194, Opus implementer running FIX ROUND R.** 194 is deployed with `OrdinaryMovesWritesEnabled` OFF; its report-only run planned 52 moves, 49 wrong (attribute-order primary; a user-owned parent's raw BU).
   - Done: base 3fc7ef2b4f (primary order, look-through, ForExistingRow, time slice); slice 1 5b320ca0ec (To Do record-first, F4 create order, K2); slice 2 21c86a7d20 (F1-b move-in when undetermined, F1-c job predicate, F1-d DR never-out, K3, K5 cache; F2 count 0); slice 3 382a2089ba (writer builders, parity tests).
   - Verifier A (isolation) + B (parity) on 21c86a7d20: not ready. Round R = ontology (c) DR question from `sprk_steps.resolvedSignalIds` (refile exemption removed; subject only); held rows lose mirror shares on Unsecure + held count; look-through cycle visited set; settle-race guard; ReparentAsync on Signal/DR; inbound comm + Compose order; ordering intrinsic to ForParents (or guard); LRA carried roots + sweep progress. Then full gates + publish delta.
   - Next: re-review the round-R diff (fresh Opus pass; isolation) → send head to ontology (re-check F1-d/F2/(c)) and ebilling (merge into 089) → PR → merge → deploy via R3 → report-only run reads clean → ontology confirms signal/DR/budgetrevision lines → owner go for writes → live check → close 194. File the K list (K-a undetermined bad data, K-b single-root slice, K-c Repair, K4). Remove C:/wt194 after (check junctions).
2. **R3 combined GHSA fix, PR #2360, head 6a051e8c52: UAC PR OK given** (after one merge-review fix, details private). Waiting: CI green → owner go in R3's session → merge (send me the sha) → deploy (separate owner go) → server gate. R3 keeps the registration resolver-branch outage-wording K privately. Remove C:/wtuacsec after the gate.
3. **Task 193 (#2338, one "Add person" entry):** starts on R3's merge sha.
4. **Task 195 (#2352 (i)+(ii), owner round 129):** the server writes a project's regarding pair (one root); N:N stays association only (three N:N cases in the POML); backfill 7 dev projects (owner go). Starts after 194b merges. Field-mapping W7/040 depend on 194b + 195 shas.
5. **Field-mapping task 037 (#2354, FR-36):** `sprk_assignedto` fill-only default from `sprk_assignedtointernal` (both contact lookups); my 4 conditions are binding; review the diff before merge.
6. **ebilling:** EB-D-162 merged into work/ebilling-r1 with EB-D-150 entry removed; live on dev (-Verify PASS). Its master PR carries both integration suites' counts.
7. **#2292** (membership junction never populated): filed; fix when there is capacity.

## Peer reviews owed (UAC is the gate)
- **ebilling:** 089 (full review on the reconciled head, after it merges 194b), 086 (2 passes), 094, 095, 098 (2 passes), 117; 091 still owes the what-if, `Test-EbillingFileStoreRole.ps1` live, and the tag-write proof. 090 K1 merge obligation (`SecuredOnlyByDeployScript` / `IsSecured=1` for sprk_ruleset, sprk_procedureassignment and 081's four tables). Standing rule: no role privilege on the Procedure tables without UAC review; D-185: External Collaborator gets none on rule set, policy or policy version.
- **field-mapping:** 017 is blocked; 018b is owed; 037 (above).
- **word-add-in task 130:** two security passes when the PR arrives.

## Owner items outstanding
1. **Task 191:** F1 and S4 block T191b onward.
2. **Task 190:** Apply, and the Restricted call on the 91 unmatched archives plus their 20 children.
3. **Decisions:** #2343 schema delete; #2245; #2250; #2260; K5 / #2270 (via R3); #2262.
4. **Optional live checks:** partner document read (D7); #2177 Outlook; #2161 re-file; T191 MDA/Copilot/grid checks.
5. **Carried:** #2163 environment-change go; 186 Tracy intake checks 3/5b/6 and the Workforce role export; 036 gates (ii)–(vi); #2153 sign-off (#2152, C:/wt2152, draft).

## Recently filed
#2350 (Classify Tier 1 timeout), #2341, #2343, #2348, #2337-#2339, #2329.
