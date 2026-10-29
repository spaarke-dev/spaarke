# Current Task State

> **Last Updated**: 2026-10-29 ~22:00Z. #1911 is MERGED to master (eaa0d44f7f, 21:33Z). Next: dev enablement (EB-D-173), once R3 deploys; 101 B2 and the Part A review are running; 104 is on work.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Master** | eaa0d44f7f = #1911 merged (094, 095, 102, 103, storedfile-owner, reconcile switch, office test-host fix). CI at e5ca635042: 48 pass, 3 skipped, 0 fail. |
| **Work branch** | work/ebilling-r1. After the merge: master merged in (8dde1822b9), then 104 merged (a4e0721bdc, ledger 147 → 148). Gate on a4e0721bdc was running (ArchTests + unit filter Finance\|Ebilling\|Billing\|Signals\|Approval\|Invoice); push when it passes. |
| **Main checkout** | Its `pull --ff-only` was REFUSED: another session's uncommitted `.claude/agent-memory/researcher/MEMORY.md` (and an untracked file) is there. Not touched. Its local master stays at c36907571d until that is resolved. |
| **Next action** | (1) Push work when the gate passes. (2) When R3 says "gate passed, you can go", set the dev app settings (below), then notify word-add-in and run the 015 dev column delete. (3) When the 101 review and B2 report back, fix any F findings, then merge task/ebill-101-corrections into work. |

## Running agents
- **ebill-101a-review** (opus, adversarial): reviewing 842110e6ab..8086bd39ef (101 Part A + EB-D-175/176).
- **ebill-101-b2** (opus): B2 re-evaluation, Signals, findings patch, status, totals, timekeeper and date corrections, `lineFindings` in the read model. Pushes to task/ebill-101-corrections.

## 101 branch state (task/ebill-101-corrections @ 33eba7f446)
- Part A: 2e4eede999, dfdc3f3a09, b66b3b5230. EB-D-175/176: 8086bd39ef. B1: 33eba7f446 (adversarial review done, F fixed; B2 seam `InvoiceCorrectionService.OnValueWrittenAsync`).
- Legacy lines (no provenance) answer 409 `ebilling.correction.no_provenance`. That follows the owner's EB-D-170; my brief had said otherwise, and EB-D-170 wins.
- B1 adds an endpoint file. Its ledger entry says 146 → 147; at the merge into work it becomes 148 → 149.

## Dev enablement (EB-D-173, owner approved 2026-10-29)
R3 deploys the dev BFF. That needs the owner's direct approval in R3's session naming the sha; I told R3 the master sha is eaa0d44f7f. After R3's "gate passed, you can go" (uac's 30-minute check), WE set the dev app settings: `Ebilling:Enabled=true`, the Finance file-store settings, and `Ebilling:StoredFileReconcileEnabled=false`. Rollback: remove them. Then notify word-add-in that 095 is live on dev, and run the 015 column delete.

## Follow-ups on work (next ebilling PR)
- ontology asks: (1) 105-override-server.md known limits must name the principal-change case; (2) SubmissionOverrideService runs RecordRefusal twice in its catch filter (double warning log).
- #2455: InvoiceAdjustmentService.cs:458 and SubmissionOverrideService.cs:1275 still use first-found CoreOf.
- 101 B2 notes: the review read model doesn't show `lineFindings` yet (B2 item 9).

## Later
- 086 / 092 / 089: merge work, then the content-team writers and the 086 fix round (uac F3-1, K1–K5; adversarial K2–K6).
- 150 (its export writer must pass `requestedBy` to CreateForAppAsync), 152. After 101: 161.
- uac: #2427 OutputLookups sha and the 194b dev-deploy notice, both to come.

## Peers
- R3: told the master sha; deploy needs the owner's approval in R3's session.
- word-add-in-r1: confirmed eaa0d44f7f; waiting for the dev notice. Told that the owner's OK is EB-D-173.
- ontology-r1, uac-r2: told #1911 merged and what went in after their approvals.
- Filed: #2426, #2454, #2455, #2460 (Office test hosts).

## Owner decisions this session
EB-D-169 to EB-D-176 are in notes/decisions-log.md.

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices. StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed (`Ebilling:StoredFileReconcileEnabled` stays false). After merges: build the 5 test projects, then run the broad filter and ArchTests before pushing. Local full-unit runs time out under machine load: re-run failing classes alone, and treat CI as authoritative. Remove worktrees only with Remove-WorktreeSafe.ps1. Max 4 concurrent subagents. Every agent launch states a model.
