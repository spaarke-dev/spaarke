# Current Task State — `unified-access-control-r2`

> **Format:** this file holds the CURRENT state only. Rewrite it at each checkpoint (never prepend), and keep it at or under 10 KB.
> - **Standing rules:** the project `CLAUDE.md`.
> - **Decisions:** `notes/session27-owner-decisions-and-research.md`, now at **round 120 (2026-10-21)**, including the round-120 gate record for task 188.
> - **Defer list:** `notes/defer-issues.md`.
> - **Delete design:** `notes/core-record-delete-design.md` (§12 = ebilling D-146, §13 = slice D Restrict, §14 = ebilling EB-D-148 Procedures).
> - **D7 / task 191 design:** `notes/2119-manage-access-all-forms-design.md` §6; `notes/2179-flagged-external-dataverse-design.md` (design checkpoint).
> - **Previous copy:** `notes/handoff-history/current-task-2026-10-20-cp57.md`.

> **Last Updated**: 2026-10-21 (checkpoint #58).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 wrap-up rules. Merge only when BOTH required checks (Router + "Client lint (type-aware ratchet)") are green. One background merge job at a time. Merge with `--squash --match-head-commit`, no `--delete-branch`, after `git merge-tree`. Deploy via R3. A task closes on merge + deploy + a server-side gate. |
| **Fix bar** | The most technically correct fix; no complexity without value. Every defect found is fixed or filed. |
| **Agents** | Opus for executors and verifiers, with every model stated. Helper scripts get a unique prefix in a private folder. Cap 4 concurrent. |
| **Dev live** | BFF `1.0.0+f1b3dadcf5` since **21:45:37Z** (R3, owner go). It carries #2170 + #2186, #2214, #2191 and #2161. Also applied on dev: Restrict on `sprk_document_RelatedCommunication_sprk_communication` and `sprk_document_ParentDocument`; field security on `sprk_relatedcommunication` / `sprk_parentdocument` (Writers = 2 BFF app users); `sprk_DocumentOperations.js` 1.28.2. Rollback snapshot: R3's `deploy/rollback/wwwroot-20261021T214302Z`. |
| **Peers** | Address peers by their ListAgents names: R3 `spaarke-wt-spa-external-access-platform-05`, ebilling `spaarke-wt-ebilling-r1-5b`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-a1`, field-mapping `spaarke-wt-field-mapping-server-write-pa-be`, provisioning `spaarke-wt-customer-provisioning-orchest-ff`, ontology `spaarke-wt-spaarke-ontology-platform-r1-79`. |

## Next actions (mine)
1. **Task 190 (D7 T7b): start.** Link legacy add-in email documents to their communication using recorded keys only. Report-only first, and list unmatched documents for the owner. FULL rigor, Opus, fresh worktree from `origin/master`.
2. **Task 191: design checkpoint.** The study is in `notes/2179-flagged-external-dataverse-design.md`. Its §5 questions were answered in rounds 114–115 (see the note's closing section):
   - design A with automatic sharing to one External Collaborators team;
   - Q3 taint = yes, using the SPA rule;
   - Q4 = about 2 minutes;
   - Q5 = a scheduled BFF move job, one-way.

   **Still open:** the carried-in question (POML goal) on scorecard, Insights and WorkspaceAi exposure for flagged externals. The design must answer it before T191a (spikes) starts. Then T191a–h per §6.
3. **Housekeeping:**
   - add #2193–#2217 to `notes/defer-issues.md`;
   - remove the merged worktrees when idle: C:/wt188, C:/wt188d, C:/wt189, C:/wt2143, C:/wt2159, plus the older C:/wt099b, C:/wt2103, C:/wt2091b, C:/wt2130, C:/wt2128, C:/wt036;
   - delete `C:/Users/RALPHS~1/AppData/Local/Temp/uac188k4/` and `uac188pub/`.

## Peer reviews owed (UAC is the gate)
- **field-mapping 016 (Repair), full diff for condition (b).** It includes:
  - the member-keyed #2170 guard (`task/fm-016` `175d5eaf04`): Type.Member exemptions, `IGenericEntityService.UpdateIfRowVersionMatchesAsync` added, a `PutBackFilingAsync` member exemption with my reason text, no Signal entries, two `DataverseUpdateHandler` private members;
  - Repair's field-security option 1 (owner chose it; 5 conditions on task 033; 052 blocked until it lands);
  - Decision Records skipped entirely; the source-secure gate fails closed.

  Also owed: 017/018 condition-(b) reviews, and 018 closes #1614/#1617.
- **ebilling gates:**
  - **081, 086 (2 passes), 089, 090, 094, 095, 098 (2 passes), 117.** 098 must cover the message threads/feed (gate + (a′), fail closed) and GET /invoice-submissions (own firm + (a′), counts as the caller).
  - **091:** the Bicep was ACCEPTED as authored (task/ebill-091-storage 8ff27228ef). Still open before 090 downloads are enabled anywhere:
    - the what-if;
    - `Test-EbillingFileStoreRole.ps1` run live;
    - `test-ebilling-file-store-tag-write.sh` run as the identity, with its output recorded in the 091 notes.

    090's code (scan time ≥ Last-Modified, with the overwrite pin) is reviewed when it arrives.
  - **Standing:** no role privilege on the Procedure tables before 191 without UAC review.
  - **Closed:** 099 accepted at 54524dead8; 080 is live on dev and verified (18 secured columns).
- **word-add-in task 130:** two security passes when the PR arrives.

## Owner items outstanding
1. **Optional live checks:**
   - #2177 Outlook: the owner saves 2 .eml from the add-in (identified → linked, empty access; unidentifiable → own-Restricted) and sends times or subjects to R3;
   - #2161: re-file an ordinary document between two matters, then R3 reads a moved count of 1;
   - D7: a partner sign-in on matter `b68299c6…` (expected 0 documents) and `86335ce3…` (3 visible).
2. **Carried:**
   - #1864: B. #2119 D1–D6: as recommended. Census: (a). #2139: (2). #2157: (a). #2164: (a). #2160 K1: (a).
   - #2163 needs an environment-change go.
   - 186: Tracy intake checks 3/5b/6 and the Workforce role export into SpaarkeMaster.
   - 036 gates (ii)–(vi): the owner's SPA steps.
3. **Sign-off to come:** #2153 (#2152, C:/wt2152, draft), after its passes.

## Done 2026-10-21 (since checkpoint #57)
- **189** closed (PR #2169, 1cc7b39ff3; live with #2150 at a10e814d02; gate PASS).
- **#2161** merged 84291ecea8 (round 119), deployed and gated (items 1–3 clean). #2206 and #2207 filed.
- **188** closed: #2170 merged 446c83d927 and #2186 merged f1b3dadcf5 (round 120); deployed; Restrict, field security and the web resource applied, all VERIFY PASS; 30-min server gate clean. K4: 11 partner-reachable own-Restricted documents on one matter are now hidden. Partner reads and BFF creates were not exercised in the window; checks are listed in round 120.
- **Peers:**
  - field-mapping #2191 (583f90c1d9) and #2214 (cf0886360a) merged and live; #2188 closed.
  - ebilling EB-D-148 dispositions are in delete design §14; 080 is live; 099 accepted; 091 option A agreed (owner).
- **Filed:** #2213 (CrossPathLink re-points an archive document's link) and #2217 (`Deploy-WebResourceInline.ps1` relative path uploads the wrong file).
