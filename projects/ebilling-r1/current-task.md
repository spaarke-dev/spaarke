# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-16
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. Done (34/78): 001-005, 010-014, 016-025, 027-029, 040-043, 045-047, 078, 108, 109. WIP (agents in worktrees, base 9db5d265e): 044 (opus), 048 (sonnet), 070 (opus), 106 (sonnet). ADR-010 ceiling 166 |
| **Step** | Waiting on the four agents |
| **Status** | in-progress (parallel mode) |
| **Next Action** | As each agent reports: cherry-pick its commits onto work/ebilling-r1 (conflicts on FinanceModule.cs / ADR010_DITests.cs: keep both sides, sum the ceiling), amend trailers to Opus 5.5 with hooks, mark ✅ in TASK-INDEX, run the build + targeted Finance tests + ArchTests. Then 107 and 026 (serial, opus), then 015 (needs owner OK, destructive) |

### Files Modified This Session
- All committed (last: 9db5d265e "start tasks 044, 048, 070, 106").

### Critical Context
- Decisions this session: D-87 (tracking number allocated when the submission row is first created, via ITrackingNumberAllocator.CreateSubmissionAsync), D-88 (duplicate firm invoice number = configurable rule, Reject or finding), D-89 (ApprovedTimekeeper holds only), D-90 (role privileges mirror invoice access; applied by scripts/ebilling/Deploy-EbillingRolePrivileges.ps1), D-91 (firm invoice number required at upload; PDF mismatch = hold finding).
- 011 done: sprk_InvoiceLineItem removed (owner removed it from the Spaarke Platform app by hand; RemoveAppComponents is a no-op there — gotcha in project CLAUDE.md).
- Task 100 constraint: line writes stay server-side; widening roles to sprk_billingevent Create/Delete is an owner decision.
- 053 deploy: 047 seeding order (Ensure-ActionRow → Deploy-ActionMirrors → BFF deploy + Seed-PlaybookConsumers -DiffOnly) and ancestor-check the live BFF build (R3 deployed 2c2fed38d, plans a6d30891a).
- 029: Activate/Deactivate hide is source-only until a release import; secure_child_ribbon.js needs a SpaarkeMaster redeploy. pac in pwsh: prepend %LOCALAPPDATA%/Microsoft/PowerAppsCLI to PATH.
- Owner action pending: add the 7 new tables to the Matter Management and Spaarke Administration apps in the app designer (save + publish).
- Open: 015 owner OK; DEF-015 Procedure Library design discussion; uac-r2 review on #1973 (reverse supersedes, supersedes-as-filing); ontology-r1 review of 109; ISS-009 CU endpoint merge gate; #2010 (DEF-019), #2011 (DEF-020); approver contacts readable by approvers (029 notes); 046 K8 residual and A7 precedence ack; write-path registry row for the tracking number not yet in DATAVERSE-WRITE-PATH-ARCHITECTURE.md; stale locked worktree dirs under C:/code_files/spaarke/.claude/worktrees.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 044, 048, 070, 106 (agents) |

## Next Action

See Quick Recovery.
