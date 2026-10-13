# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-13 (ADR-028 A9 applied; PR #1810 open with auto-merge)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Cross-project: ADR-028 **A9** (workforce contacts sign in with their customer's home directory), owner-approved 2026-10-13. R3 owns it end to end (SPA client + BFF token acceptance + registration + stored link); UAC owns only baseline role access. 038: A+B done (R3 live on dev via `99ec749cd`); phase C-W done except C-W6 (intake 403, owner fix pending) and C-W8b; C-C waits on the SPA deploy. 040 SPA deploy HELD: superseded by the A9 re-plan |
| **Step** | A9 applied to ADR-028 (+A1/A2/A8 notes), `.claude/adr/INDEX.md`, `.claude/constraints/auth.md`; coordination note + draft updated; project CLAUDE.md §2-§4 updated. Commit `7a1b8b4e2` pushed; **PR #1810** to master, auto-merge on (also carries `78e7b16d6` A8 CIAM-audience clarification + 038/040 records + research). Per-project messages sent 2026-10-13 to UAC `-1d`, word-add-in `-46`, provisioning `-b2`, asking each to record it in its notes + CLAUDE.md and confirm |
| **Status** | PR #1810 waiting on CI. Uncommitted: project CLAUDE.md + this file (commit next). Main checkout `C:\code_files\spaarke` NOT synced (another session's work); leftover folders to delete by hand: `C:\code_files\spaarke\.claude\worktreesgent-af842f88050a4b5b9`, `...gent-ae8f4415f1670de13`, `C:\wt039bz` |
| **Next Action** | Watch PR #1810 merge; collect the three confirmations; then re-plan tasks (040 multitenant browser client + DEF for Teams; 041 home directory id; 056 profile form; withdraw 012/060; new BFF tasks: token acceptance, registration endpoint, stored link) and a BFF design note for owner sign-off |

### Critical Context
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` (`git reset --hard <R3 sha>` first), commit there, main session merges `--no-ff`; after a merge re-verify ledger line cites (the guard only checks format). SPA tasks run in this worktree, uncommitted; the main session commits by explicit path. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two fable passes; other FULL tasks one. Fix rounds sonnet; a fix that changes approach gets a scoped re-check. Max 4 agents.
- **Bookkeeping edits:** python via PowerShell here-strings (sed breaks on the status emoji). Check with `pwsh -NoProfile -File scripts/check-task-status-drift.ps1`.
- **Do not run the full BFF unit suite under load**; use filtered runs. Bash is very slow on this machine: prefer PowerShell, Grep, Read.

## Owner answers 2026-10-12 (recorded in project CLAUDE.md §3)
Option B; no delete after close; invoice upload = invoice rights only; team-access and renewal bell items wanted; contact owned by the customer's primary BU; intake (department from contact, Invention partners behind a flag, incidents simple, fixed lists, NDA review + issue); grid views from Dataverse views; 060 shared service OK if Model 1 allows; dev dispatch "ok"; 060 tenant-wide group permission approved if it cannot be scoped. Deferred: DEF-001 #1736 NDA generation, DEF-002 #1737 incident alerting.

## Open owner questions
None open. S0a (CIAM `user_impersonation`) DONE 2026-10-13: the scope exists, the client is authorized, and admin consent is granted (record in `notes/task-040-spa-dev-deploy-plan.md`).
Intake 403: FIXED 2026-10-13 (`5bfedfe3d` + a dev role change; C-W6 201, C-W8b 204; record in `notes/task-038-deploy-record.md`).
Answered 2026-10-13 (project CLAUDE.md §3): grid views, bell items, ISS-005, 052, 053, licensing (outside the project), mechanical guard (not added). UAC confirmed A9 by the owner directly.

## Option B known limits (notes/task-039-notes.md §12.11) (notes/task-039-notes.md §12.11)
Messages and feed give no "incomplete" signal; `Truncated` may over-report; a project pair naming a deleted matter stays secure permanently; `IGenericEntityService` filing seam is DI-supplied (drop = over-hide, not leak).

## Next actions (in order)
1. PR #1810 merges (auto). Then sync: the worktree is current with master.
2. Collect confirmations from UAC, word-add-in, provisioning; record any objection.
3. A9 re-plan via task-create: 040, 041, 056, withdraw 012/060, new BFF tasks; DEF for Teams-for-workforce-contacts via `/project-defer-issue-tracking`.
4. BFF design note (A9-cited) for owner sign-off (auth code; two adversarial passes).
5. Grid views task once question 5 is answered.

## Dependencies tracked elsewhere
- ISS-001 (#1623), ISS-002 (#1633), ISS-003 (#1636), ISS-004, ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
