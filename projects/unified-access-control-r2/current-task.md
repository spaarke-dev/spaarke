# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-05 ~22:15Z (0f53e053bf deployed to dev, gate PASS; 043 cleared; 049 decisions sent).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; peers take the owner's go for their own live writes in their sessions. |
| **Active work** | Reviews (below). No deploy in flight. No uncommitted files. |
| **Dev state** | BFF = **0f53e053bf** since 2026-11-05T21:39:06Z (gate PASS; record `notes/deploy-gate-2026-11-05-0f53e053bf.md`; gate script `$TEMP/uacmerge/uacg/gate0f5.sh`). Rollback snapshot `deploy/rollback/wwwroot-20261105T213757Z` is 909754869f. Dev SPA = b083d62183. |
| **Next action** | 1. fm 049b: review when fm sends the final head SHA (fix round in progress, with my decisions below). 2. 046b -Apply on dev when fm's script and WhatIf are ready (owner go given for G1, G2, G4-G8). 3. ebilling master PR review when it opens. 4. Owed to ontology: Signals 200 and the nightly counts on 0f53e053bf. |

## Reviews in flight
- **ebilling** (`-c7`):
  - 082c-2 is merged into work (ef6eb01910); C19 and P2 blank-firm are cleared; DEF-044 is merged into work (1c581b33ae).
  - Master PR coming. My items on its checklist: the pre-P2 table, the `sprk_trackingscope` re-grep, the runbook lines, prevseq MappingProtectedColumns, C22, the full integration suites.
  - After it: my deploy, P2 migration (owner go in their session), seam test, L6 Defender check, before-notice, then I flip `Ebilling__StoredFileReconcileEnabled` (owner go HERE).
  - Answered: D-142 override Global Write is intended; the "New Billing Event" ribbon gets hidden. DEF-053 (#2571) filed. 082c-3 (08702087cb) and 114 (c4b8d43e8f) come with the master PR.
- **field-mapping** (`-69`):
  - **043** (`de96647095`): cleared, no F (Ks: push timing, older log lines, analysis-create warnings widened). Re-check only a further delta if fm pushes one.
  - **046/046b:** fm builds the role files and the apply script (-WhatIf/-Apply/-Verify; root role through the Web API; read back every BU copy; KnownGaps removed); fm runs -WhatIf. I run -Apply on dev under the owner's go, given 2026-11-05, for:
    - G1: Create, Append and AppendTo on reportcard (Core);
    - G2;
    - G4;
    - G5: file alignment;
    - G6: Office Add In gets Write on document (Deep);
    - G7/G8: Office Add In gets AppendTo on account and sprk_budget (Deep).

    The standalone add-on gap list goes to the owner before anything from it is applied. The guard pins Ontology Administrator as the only person role with C/W/D on the writer-only tables.
  - **049b** (`task/fm-049b`, fix round in progress). My decisions, sent:
    - the maker-as-assignee pass stays inline, with a test pinning both sides;
    - only the removal paths take the lease; fm's notes must show the reconcile re-derives Assigned-To grants, or the gap is filed;
    - in scope: the `ScanAssignedRootsAsync` cap (a uac defect): `$orderby` plus a recent-change window ≥3× the tick;
    - in scope, PRIVATE wording: isolated creates get no create-time pass; `CompleteIsolatedCreateAsync` runs it after isolation. The wizard's secure project is unchanged: fm verifies in its notes that provisioning doesn't change eligibility and that pre-move grants survive. If either fails, it becomes an fm issue (task 020);
    - Ks accepted: no invite when the reconcile grants first; duplicate passes collapse; `record-busy` acceptable only if the user sees a message, not a 500.
  - 044 field-security profile still owed (owner go here).
- **ontology** (`-38`): owed TO them: Signals 200 from organic traffic, and the nightly suppression and Undetermined counts on 909754869f and on 0f53e053bf (live since 21:39:06Z).

## Owner items outstanding
- **NEW (ontology recommendation): column auditing on the writer-only tables' lookups.**
  - Today the org and the three tables audit, but no custom lookup column is audited.
  - At least: Signal (sprk_matter, sprk_decisionrecord, sprk_policy, sprk_policyversion); DR (sprk_matter, sprk_project, sprk_policyversion); BudgetRevision (sprk_budget, sprk_matter).
  - This is a solution schema change: it needs the owner's go and a notice to customer-provisioning. Not asked yet.
- The person-path refusal uniformity question (private, K).
- Email to AP diagnostics (batch 14680609-b198-48e2-8391-3ada84fda689): the owner's URL, an InPrivate retry, and ribbondebug.
- VS Code agent-dir cleanup OK (`C:/code_files/spaarke/.claude/worktrees`, 123 dirs; Remove-WorktreeSafe; skip anything used in the last 24h and the dirty ones).
- word-add-in: the owner's Office saves and Submit Invoice (read-back after the deploy notice).
- 095a lookup-name question; click-test 195; Q1/Q4 private items.
- Older: #2343, #2245, #2250, #2260, #2270, #2262, #2396, 191 F1/S4, 190 Apply, #2382, #2505; tasks 140/165/153.

## Decided today (in notes/decisions.md)
- Flagged licensed users read DR and Signals per their roles; no change.
- 046 role rulings: the additions; Office To Do and the Do lane stay Core-only; add-ons may be standalone; Ontology Administrator is the cleanup role (D-160 stands; auditing verified on at table level).
- D-204 extended to chat delete (fm 048, merged).

## Private items (never in issues/PRs/commits)
- Core pair, lineage and update paths: merged in #2568 and deployed; read-back unchanged.
- The push runs as the caller (#2253 K2). The push's lack of external narrowing and the 404 timing are known limits of the round-77 class (fm notes only).

## My own backlog
- POST /api/v1/documents: route-ledger Pending (owner 166, medium). The body's ContainerId has no caller check and no Create privilege is asked. Caller: `SummarizeAnalysisStep.tsx:109`. Fix: G5 Create check plus a server-derived or validated container. fm asked me to classify it: it's mine.
- #2560, #2524, gate null-column hardening, SharePoint census, #2543, #2292, K-P1 for task 191; parked: 193 (C:/wt193), 197, #2427, 196a/196b; TASK-INDEX reconcile at 090.

## Peers
uac-r2 `-20` (this session) · ebilling `-c7` · field-mapping `-69` · ontology `-38` · provisioning `-b8` · word-add-in `-7b` · website `spaarke-website-cb` · SPA R4 `spaarke-wt-spa-external-access-platform-ad` (copy it on SPA invoice or record-modal UI changes).
