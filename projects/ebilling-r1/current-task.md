# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-15
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. Done: 001-005, 010-014, 016-025, 027-029, 040-043, 108, 109. Ready: 015 (destructive: owner OK), 026, 045, 046, 047, 070, 078, 106, 107. ADR-010 ceiling 162 |
| **Step** | — |
| **Status** | in-progress (parallel mode) |
| **Next Action** | Launch the next wave (045/046/047/078 P2-B, 106/107 P4-A, 070, 026, 015 prepare-only). Owner asks pending: role privileges on the new eBilling tables (Spaarke roles lack read, e.g. sprk_invoiceapproval 0x80040220); add the 7 new tables to the Matter Management and Spaarke Administration apps in the app designer (AddAppComponents is a silent no-op) |

### Files Modified This Session
- All committed (last: 06c5c546d). 011 script, notes §8, rows backup JSON in f018e6dc0.

### Critical Context
- 029 done 2026-10-16 (notes/029). Activate/Deactivate hide is source-only until a release import; secure_child_ribbon.js needs a SpaarkeMaster redeploy; sprk_amount read-only applies to all line types (form-level, no script per ADR-002). DEF-019 #2010: identity contact key unmapped (pre-existing; fired the PlanOnly trigger). For pac in pwsh, prepend the PowerAppsCLI folder under %LOCALAPPDATA%/Microsoft to PATH.
- 013 follow-up hand-off posted on #1973 (uac-r2 to review OwnedChildWrite/ChildRecordEndpoints/handlers/ribbon; decide reverse-supersedes and supersedes-as-filing). The ribbon web resource needs a SpaarkeMaster redeploy (029 or a deploy task).
- Open: DEF-015 design discussion (procedure files storage); ontology-r1 review of the 109 PR; ISS-009 CU endpoint merge gate; #1954 optional. Stale locked worktree dirs under C:/code_files/spaarke/.claude/worktrees (incl. agent-a108089d67799cec8).

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |

## Next Action

See Quick Recovery.
