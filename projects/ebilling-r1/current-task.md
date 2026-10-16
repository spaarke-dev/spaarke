# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-15
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. Done: 001-005, 010, 012-014 (incl. ISS-010 follow-up, cherry-picked 7fc7518a2/38987e4d4/06c5c546d), 016-025, 027, 028, 040-043, 108, 109. WIP: 011 (dev delete half-run). Open-and-ready: only 029, which is blocked by 011. ADR-010 ceiling 162 |
| **Step** | 011: dev delete stopped at step 5 (Spaarke Platform app still lists the table) |
| **Status** | blocked on owner action (app designer) |
| **Next Action** | Owner removes the Invoice Line Item table from the Spaarke Platform app in make.powerapps.com (Save + Publish). Then re-run `pwsh scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1 -Delete -DeleteRows -OwnerApproval '2026-10-15 owner (Ralph Schroeder) via Claude session' -EnvironmentUrl https://spaarkedev1.crm.dynamics.com`, record output in notes/011 §8, mark 011 done, then start 029 |

### Files Modified This Session
- All committed (last: 06c5c546d). 011 script, notes §8, rows backup JSON in f018e6dc0.

### Critical Context
- 011 in dev: rows backed up, forms edited, timekeeper lookup, AI skill configs and 2 Copilot table-search entries deleted; 4 rows + table remain. RemoveAppComponents is a silent no-op on sprk_SpaarkePlatform (project CLAUDE.md §6 gotcha).
- 013 follow-up hand-off posted on #1973 (uac-r2 to review OwnedChildWrite/ChildRecordEndpoints/handlers/ribbon; decide reverse-supersedes and supersedes-as-filing). The ribbon web resource needs a SpaarkeMaster redeploy (029 or a deploy task).
- Open: DEF-015 design discussion (procedure files storage); ontology-r1 review of the 109 PR; ISS-009 CU endpoint merge gate; #1954 optional. Stale locked worktree dirs under C:/code_files/spaarke/.claude/worktrees (incl. agent-a108089d67799cec8).

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 011 |
| **Task File** | tasks/011-*.poml |
| **Title** | Retire `sprk_invoicelineitem` |
| **Status** | in-progress (blocked on owner app-designer step) |

## Next Action

See Quick Recovery.
