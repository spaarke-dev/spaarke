# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-13 (A9 tasks created; check-2 fixes 061–063 created)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | ADR-028 **A9** build (owner-approved design `notes/a9-bff-design.md`, Teams included; ADR amended `63b7c575b`). R3 owns it end to end. Owner check-2 fixes 061–063 |
| **Step** | 2026-10-13: A9 tasks created (044–048, 058, 059, 074–076); 060 cancelled; 012/056 superseded-in-part notes; 071 widened (9 Teams runs); check-2 fixes 061/062 (startable), 063 (blocked: owner decision on app-only enqueue rule); 002 gained grid-config labels (issue 2). Drift check + POML validator green. SPA deployed to dev (run `38420637859`), S2a pass; owner checks 1–2 done |
| **Status** | Commits after #1810 (`5bfedfe3d`, notes, `63b7c575b`, task creation) are on the branch, not on master: next PR needs `/conflict-check` (word-add-in PR #1815 may edit `.claude/constraints/auth.md` line 69, adjacent to ours) |
| **Next Action** | Answer owner check 3 (partner access via "Manage Access"); run the approved issue-3 read-only check (scratchpad issue3-numbering-read.ps1, after showing it); 063 unblocked (owner: index + profile); then run A9 Wave 1 (044 + 045) and CHK2 (061 + 062) |

### Critical Context
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` (`git reset --hard <R3 sha>` first), commit there, main session merges `--no-ff`; after a merge re-verify ledger line cites (the guard only checks format). SPA tasks run in this worktree, uncommitted; the main session commits by explicit path. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two fable passes; other FULL tasks one. Fix rounds sonnet; a fix that changes approach gets a scoped re-check. Max 4 agents.
- **Bookkeeping edits:** python via PowerShell here-strings (sed breaks on the status emoji). Check with `pwsh -NoProfile -File scripts/check-task-status-drift.ps1`.
- **Do not run the full BFF unit suite under load**; use filtered runs. Bash is very slow on this machine: prefer PowerShell, Grep, Read.

## Owner answers 2026-10-12 (recorded in project CLAUDE.md §3)
Option B; no delete after close; invoice upload = invoice rights only; team-access and renewal bell items wanted; contact owned by the customer's primary BU; intake (department from contact, Invention partners behind a flag, incidents simple, fixed lists, NDA review + issue); grid views from Dataverse views; 060 shared service OK if Model 1 allows; dev dispatch "ok"; 060 tenant-wide group permission approved if it cannot be scoped. Deferred: DEF-001 #1736 NDA generation, DEF-002 #1737 incident alerting.

## Open owner questions
1. Check-2 issue 3 ("Number —" on SPA-created service requests): the repo has no numbering (no autonumber, plugin, flow or script); MDA rows get RQST-0001 from something live-only. Needs a read-only look at the dev column (AutoNumberFormat) and flows on `sprk_servicerequest`.
2. Owner check 3 (CIAM partner, 2026-10-13): documents fail on a newly granted matter; no document preview; grid row not selectable / no download; no +New; partner does not see MDA-sent messages. Code investigation running; App Insights read-only query offered (awaiting OK).
3. Owner deletes probe message `b38f0ab7-03c7-f111-a05c-0022482913fc` by hand.

## Option B known limits (notes/task-039-notes.md §12.11) (notes/task-039-notes.md §12.11)
Messages and feed give no "incomplete" signal; `Truncated` may over-report; a project pair naming a deleted matter stays secure permanently; `IGenericEntityService` filing seam is DI-supplied (drop = over-hide, not leak).

## Next actions (in order)
1. Commit + push task creation; PR the post-#1810 commits (after `/conflict-check`).
2. Owner answers above; then A9 Wave 1: 044 (owner-gated dev import) + 045 (opus, 2 adversarial passes); CHK2: 061 + 062.
3. A9 Wave 2: 046 (rebase onto UAC 185 if merged) + 059 (live, owner-gated).
4. 047 → 048 → 058 → 075 → 076 → 074; then 070/071/072.

## Dependencies tracked elsewhere
- ISS-001 (#1623), ISS-002 (#1633), ISS-003 (#1636), ISS-004, ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
