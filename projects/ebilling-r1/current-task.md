# Current Task State

> **Last Updated**: 2026-10-30 (context-handoff). #1911 is on master (eaa0d44f7f). Task 101 has just been merged into work locally (de4e296242, NOT pushed, gate NOT run yet). Tasks 150 and 152 are running.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work branch** | Local work/ebilling-r1 = **de4e296242** (merge of task/ebill-101-corrections @ 1d74ce97fd; the ledger conflict is resolved to **149**, a single count line). origin/work = b8912e5553. **Unpushed.** |
| **Next action** | (1) Build the 5 test projects, then run ArchTests and the unit filter `Finance\|Ebilling\|Billing\|Signals\|Approval\|Invoice` on de4e296242. Full-unit runs time out locally under load; re-run failing classes alone. (2) Push work. (3) Add the 101×104 twin test on work: reject/return refused on a held invoice after a correction changes the held group. Also decide whether reject/return should honour `corrections_pending` (not required: a rejected invoice pays nothing). (4) Update TASK-INDEX: 101 ✅, plus the 050a/100/105/115 statuses (their notes show them implemented; verify). |
| **Master** | eaa0d44f7f (#1911). Main checkout `C:\code_files\spaarke` NOT fast-forwarded: another session's uncommitted `.claude/agent-memory/researcher/*`. Left alone. |

## Running agents
- **ebill-150** (sonnet), branch task/ebill-150-ap-export from work f39d7f127b: AP export batch. The owner chose EB-D-178: Create on `sprk_billingexport` + `ebilling.invoice.decide` on every invoice, behind ONE seam (`AuthorizeExportAsync`) so a dedicated right can come later. Ledger: write it as 148 → 149; at the merge it becomes 149 → 150.
- **ebill-152** (sonnet), branch task/ebill-152-invoice-index: invoice index from structured records. No live AI Search.
- After a merge: build the 5 projects, then run ArchTests and the unit filter, then push. One independent adversarial review per change set (150 touches money, so review it).

## 101 (done, merged locally)
- Branch tip 1d74ce97fd. Final gate: 5 builds; ArchTests 1858/0; unit filter 9061/0/7; integration 199/0/8; publish +0.12 MB.
- Review F-1..F-5 closed (F-4 kept by EB-D-175).
- Approval refuses 409 `ebilling.approval.corrections_pending`, and a line decision refuses 409 `ebilling.line.corrections_pending`, while a correction's whole follow-through hasn't finished. Accept all Clean refuses if any correction on the invoice is unfinished.
- The approval GET carries no reason for `canApprove=false` (a contract addition for 110/111 to decide).
- Main-session calls: alerts close with the record-free CloseAsync (POML); the hold clears when no undecided line lacks a code set; currency re-runs the money rules (no mismatch code); legacy lines answer 409 (EB-D-170); the diff starts from the last landed patch (`patchedCorrections`).

## Dev enablement (EB-D-173, owner approved)
R3 deploys the dev BFF from master ≥ eaa0d44f7f (needs the owner's direct approval in R3's session, naming the sha). After R3's "gate passed, you can go" (uac's 30-minute check), WE set `Ebilling:Enabled=true`, the Finance file-store settings, and `Ebilling:StoredFileReconcileEnabled=false`. Rollback: remove them. Then notify word-add-in (waiting) and run the 015 dev column delete. No word from R3 yet.

## Next tasks (deps satisfied)
097 (opus; corrected invoice supersedes; deps 094, 104, 088). 086 / 089 / 092 branches: merge work, then the content-team writers and the 086 fix round. 161 after 101. 151 after 150. 096 waits on word-add-in removing the add-in side.

## Follow-ups / filed
#2426, #2454, #2455 (now closed in code: line decisions via 101 Part A, adjustment and override via followups; close the issue after work → master), #2460. Task 110 POML has the lineFindings criterion.

## Owner decisions this session
EB-D-169 to EB-D-178 are in notes/decisions-log.md (EB-D-177 came via ontology D-200; EB-D-178 is AP export).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices. StoredFileReconcileJob stays off (`Ebilling:StoredFileReconcileEnabled=false`) until F4 merges and DEF-044 is fixed. After merges: build the 5 test projects, then run the broad filter and ArchTests before pushing. Remove worktrees only with Remove-WorktreeSafe.ps1. Max 4 concurrent subagents. Every agent launch states a model. No bare jq, no grep -P.
