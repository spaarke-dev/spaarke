# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md` (round 125 is the latest owner action: #1830 container removed, 65a3fab2 re-provisioned).
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146, §13 = slice D Restrict, §14 = ebilling EB-D-148 Procedures).
> - **Task 191 design:** `notes/2179-flagged-external-dataverse-design.md` (last section: D-185 input).
> - **Last deploy + gate:** `notes/deploy-gate-2026-10-23-1fa0cf5cb8.md`.
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-23 (checkpoint #61).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 wrap-up rules. Merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green. One background merge job at a time. Merge with `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. Public repo: no live exploit mechanism in issues, PRs, commits or committed notes. |
| **Agents** | Opus for executors and verifiers, with every model stated. Helper scripts get a unique prefix in a private folder. Cap 4 concurrent. |
| **Dev live** | BFF `1.0.0+1fa0cf5cb8` since **2026-10-23 00:00Z** (R3, owner go). Gate PASS. Rollback: R3 `deploy/rollback/wwwroot-20261022T235642Z`. UAC content: #2253, #2268, #2258, #2277. |
| **Peers** | R3 **`spaarke-wt-spa-external-access-platform-08`** (renamed after a restart), ebilling `spaarke-wt-ebilling-r1-5b`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4`, field-mapping `spaarke-wt-field-mapping-server-write-pa-be`, provisioning `spaarke-wt-customer-provisioning-orchest-ff`, ontology `spaarke-wt-spaarke-ontology-platform-r1-79`. |
| **Gate tooling** | App Insights `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`), role `spaarke-bff-dev`. Pass `--offset Nd` (default look-back is 1 h). Use `contains`, not `has_any`, for exception names. Script: temp `uacmerge/uacg/uacg_gate.sh`. |

## Next actions (mine)
1. **ebilling F4/F3 round: ACCEPTED at 4fa63402de (2026-10-23)**, with review worktree C:/wteb9156 removed. When ebilling merges to master it takes #2301. The history below is kept for reference:
   - Done: both merges are clean (57f22fac9a master, 91568010c4 for 090), and conditions 1–3 hold.
   - ebilling is fixing three items:
     - F1: add `sprk_regardingrecordtype` / `sprk_regardingrecordid` to the sprk_signal AccessColumns;
     - F4: three false statements;
     - #2294: number-only Duplicates show the generic sentence; `line_same_invoice` is judged by its own invoice inside the bound, on SignalReadService and InvoiceReviewReadService.
   - Re-check the range when it arrives, then remove C:/wteb9156.
   - F2 (UAC's own #2277 matcher used a substring match) is fixed by **#2301, merged 39306ac044** (quoted-token match). It is test-only; ebilling takes it in its next master merge.
2. **R3 ISS-005: the guard is ACCEPTED at c05d1b2788 (round 6).** It stays unmerged until the owner signs off the dedicated contact owner team design. R3 will then send the design-change diff for UAC review. The pre-existing finding is tracked privately as GHSA-hq7m-w2pf-v8j7. The defer list carries the reference only; never add the mechanism.
3. **Ontology task 163 (PR #2298, D-185):** K4 is accepted for internal callers. Condition: no app-only policy enrichment for an external or unknown caller, mirrored privately in `SignalReadService` from master's `IsExternalOrUnknownCallerAsync` (do not copy the Filters file). Ontology accepted it and its pass 2 checks it. Ontology will ping before and after the live deploy (BFF, then config, then role) with the #1355 read-back; then run the server-side gate.
4. **Dev BFF deploy gate (R3):**
   - **0317579272** (ontology 159/161/163): UAC OK, on condition Router and Client lint are green.
   - **UAC OK for the first master SHA containing #2308** (8c8205c95a, F2/F3 fix, accepted 2026-10-23), once Router and Client lint are green on it. Word-add-in has an open follow-up: answer a `system_failure` 403 as 503. Any SHA with #2304 but without #2308 stays held. Earlier hold:
     - F2: the email identity probe answers an unreadable Message-ID match (403) differently from no match (200 not_saved);
     - F3: no tests for refused access.

     Re-check their fix PR, then re-OK. K1–K3 are to be filed by word-add-in.
   - #2309 (db840611a3, plus the comment-only eeba1ae650) is accepted too. The OK covers master up to #2308 and #2309.
   - **HOLD on any SHA containing ontology #2313 (task 160)**, for F1: the inquiry footer carries a Restricted matter's name and number to external recipients (InquiryRecordFacts.cs:248, BudgetInquiryExecutor.cs:117). The fix must gate on Standard plus all-internal recipients, with `sprk_isexternal` checked (K1 is folded into it). K2: Sent Items shows whether an address is a Spaarke user. Re-check ontology's fix PR.
   - Run the server gate after whichever SHA deploys.
5. **Basic User (11f93c04) lost `prvReadsprk_Policy` and `prvReadsprk_ruleset`** at 2026-10-23 00:01:07Z (MCP times are local EDT). modifiedby is the owner's account; there was no import.
   - Ruled out: UAC, R3, ebilling, ontology, word-add-in and the BFF.
   - Ontology tracks it in its security-roles §19. Not re-granted, because D-185 removes it anyway.
6. **#2292 (membership junction never populated: an alternate-key miss throws instead of creating).** Filed; not an access input. Fix it when there is capacity, or leave it filed.

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
