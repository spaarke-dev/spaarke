# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-05 ~19:45Z (checkpoint #96: dev deploy of 0f53e053bf being prepared; 046 role rulings; reviews in flight).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; peers take the owner's go for their own live writes in their sessions. |
| **Active work** | **Dev BFF deploy of master `0f53e053bf`** (#2569 fm 042c + #2570 fm 048; Router green; 909754869f is an ancestor; src/server 13 files +231/−9). The owner said "deploy now, master as it is" but the go must NAME the SHA: ask for it once ebilling OKs. One Opus review agent is running (fm 043 access review). No uncommitted files. |
| **Dev state** | BFF = **909754869f** since 2026-11-05T04:16:30Z (gate PASS; record `notes/deploy-gate-2026-11-05-909754869f.md`). Rollback snapshot `deploy/rollback/wwwroot-20261105T024806Z` is d763dc120e. Dev SPA = b083d62183. |
| **Next action** | 1. Wait for ebilling's OK on 0f53e053bf (fm, word-add-in, ontology OK; provisioning standing). 2. Ask the owner for the go naming `0f53e053bf`. 3. Run the checklist below. 4. Answer fm on 043 when the review agent reports. |

## Deploy 0f53e053bf: checklist
- **Done (read-only):** 225 settings; the four switches (RootOwnerWrites, OrdinaryMovesWrites and Ebilling__Enabled true; StoredFileReconcile false); the 3 `Finance__FileStore__*` names present.
- **Before the deploy:**
  - the #2450 ancestor check and no non-comment `sprk_invoicestatus` under `src/server`;
  - `Set-ChildRecordCreatorPersonSchema.ps1 -Verify`;
  - a detached worktree at the SHA (C:/wtdep is gone; recreate it);
  - `Save-BffDevRollbackSnapshot.ps1 -RepoRoot <this worktree>`;
  - the pre-deploy count of `record_creation.table_unsupported` over the prior 24h;
  - a before-notice to word-add-in, R4 (`spaarke-wt-spa-external-access-platform-ad`), ontology, fm and ebilling.
- **Deploy and gate:**
  - `Deploy-BffApi.ps1 -Environment dev`, then `Test-BffDevDeployReadback.ps1`;
  - the gate script `$TEMP/uacmerge/uacg/gate909.sh <start> <end>`;
  - extra gate checks:
    - 0 from organic traffic for `record_update.changed_only_by_its_writer` and `record_delete.changed_only_by_its_writer`;
    - `record_creation.table_unsupported` compared with its pre-deploy count, NOT with 0;
    - no 5xx on `/api/v1/signals*` or `POST /api/v1/signals/{id}/decision`;
    - 0 5xx on the create, update and chat routes;
    - 0 OWNERSHIP-REFUSED.
  - A refusal from a WRITER path reopens the deploy.
- **After:** an after-notice with the receive time (word-add-in needs it for the owner's Office saves and Submit Invoice read-back); a record in `notes/deploy-gate-2026-11-05-0f53e053bf.md`; Remove-WorktreeSafe.

## Reviews in flight
- **ebilling** (`-c7`):
  - 082c-2 is merged into work (ef6eb01910); C19 and P2 blank-firm are cleared; DEF-044 is merged into work (1c581b33ae).
  - Master PR coming. My items on its checklist: the pre-P2 table, the `sprk_trackingscope` re-grep, the runbook lines, prevseq MappingProtectedColumns, C22, the full integration suites.
  - After it: my deploy, P2 migration (owner go in their session), seam test, L6 Defender check, before-notice, then I flip `Ebilling__StoredFileReconcileEnabled` (owner go HERE).
  - Asked, no answer yet: the deploy OK; whether Core User's Write on `sprk_ebillingoverride` at Global on dev (not in the role file) is intended; the "New Billing Event" ribbon (no role creates lines).
- **field-mapping** (`-69`):
  - **043** (a document as a mapping source, `de96647095`): the Opus access review agent is running; send the verdict by message.
  - **046/046b:** fm builds the role files and the apply script (-WhatIf/-Apply/-Verify; root role through the Web API; read back every BU copy; KnownGaps removed); fm runs -WhatIf. I run -Apply on dev under the owner's go, given 2026-11-05, for:
    - G1: Create, Append and AppendTo on reportcard (Core);
    - G2;
    - G4;
    - G5: file alignment;
    - G6: Office Add In gets Write on document (Deep);
    - G7/G8: Office Add In gets AppendTo on account and sprk_budget (Deep).

    The standalone add-on gap list goes to the owner before anything from it is applied. The guard pins Ontology Administrator as the only person role with C/W/D on the writer-only tables.
  - **049:** conditions sent; waiting for the diff.
  - 044 field-security profile still owed (owner go here).
- **ontology** (`-38`): owed TO them: Signals 200 from organic traffic, and the nightly suppression and Undetermined counts on 909754869f (and on 0f53e053bf once live).

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
