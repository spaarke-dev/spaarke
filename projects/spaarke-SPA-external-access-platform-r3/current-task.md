# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-13 (R3 merged to master via PR #1795)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 038: phases A+B done 2026-10-13. R3 is live on dev through the word-add-in master deploy `99ec749cd`, verified by R3 (live DLL sha on master, contains `ba1242b5a`, health 200). Next: phase C probes (need owner approval; show commands first). Pending owner decision: task 040 SPA deploy to dev (word-add-in's Dewey Cheatham retest waits on it; needs the task 041 CIAM API app `user_impersonation` scope) |
| **Step** | PR #1795 merged to master 2026-10-13T03:35Z (merge `ba1242b5a`): master merged into R3 first (5 conflicts resolved, absent-intermediate read-path fix, message-send test seam), ADR-028 R3 amendment renumbered A8 + sweep. Verified pre-merge: arch 969, filtered unit 4771, SPA 993. After merge, pushed `78e7b16d6` (A8 clarification: CIAM plane requests the per-customer CIAM API app `ciamBffAppId`; owner-approved) - NOT yet on master, rides the next PR |
| **Status** | Worktree clean, in sync with origin and current with master (merged `ba1242b5a` 2026-10-13; clean). Main checkout `C:\code_files\spaarke` NOT synced: `pull --ff-only` refused (another session's 4 modified BFF files + untracked `tests/Spaarke.ArchTests/WebResourceMirrorParity.baseline.txt`); left untouched. Locked leftover folders to delete by hand: `C:\code_files\spaarke\.claude\worktrees\agent-af842f88050a4b5b9`, `...\agent-ae8f4415f1670de13`, `C:\wt039bz` |
| **Next Action** | 038 (deploy + live probes) after approval -> 070 needs 043 owner inputs; grid views task after owner answers question 1 |

### Critical Context
- **Parallel pattern:** BFF tasks run in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*` (`git reset --hard <R3 sha>` first), commit there, main session merges `--no-ff`; after a merge re-verify ledger line cites (the guard only checks format). SPA tasks run in this worktree, uncommitted; the main session commits by explicit path. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation changes get two fable passes; other FULL tasks one. Fix rounds sonnet; a fix that changes approach gets a scoped re-check. Max 4 agents.
- **Bookkeeping edits:** python via PowerShell here-strings (sed breaks on the status emoji). Check with `pwsh -NoProfile -File scripts/check-task-status-drift.ps1`.
- **Do not run the full BFF unit suite under load**; use filtered runs. Bash is very slow on this machine: prefer PowerShell, Grep, Read.

## Owner answers 2026-10-12 (recorded in project CLAUDE.md §3)
Option B; no delete after close; invoice upload = invoice rights only; team-access and renewal bell items wanted; contact owned by the customer's primary BU; intake (department from contact, Invention partners behind a flag, incidents simple, fixed lists, NDA review + issue); grid views from Dataverse views; 060 shared service OK if Model 1 allows; dev dispatch "ok"; 060 tenant-wide group permission approved if it cannot be scoped. Deferred: DEF-001 #1736 NDA generation, DEF-002 #1737 incident alerting.

## Open owner questions
1. **Grid views:** (a) "assigned resolver" = the shared membership resolver? (b) views per entity = External Active / My / Inactive? (c) log the internal "My" views (`ownerid eq-userid`) as an issue?
2. **Bell items:** team access A (none) / B (snapshot diff in cache); renewals A (accept modifiedon noise) / B (`sprk_renewedon` column).
3. **ISS-005:** option A, `ExternalAccess:CustomerBusinessUnitId` written by provisioning H4b from H10's BU id.
4. **060:** approve (1) the D-12 clarification, (2) the ADR-028 A8 carve-out for the `/organizations` registration client, (3) 60-day log retention. Analysis: `notes/task-060-model1-shared-service-analysis.md`.
5. **052:** Service Requests as a partner default tab (opens first) or library-only? Accept no intake idempotency key as a known limit?
6. **053:** optional "classes, if you know them" field on Trademark (design §4 deliberately omits classes)?

## Option B known limits (notes/task-039-notes.md §12.11)
Messages and feed give no "incomplete" signal; `Truncated` may over-report; a project pair naming a deleted matter stays secure permanently; `IGenericEntityService` filing seam is DI-supplied (drop = over-hide, not leak).

## Next actions (in order)
1. 038 BFF deploy to dev + live probes (show commands + rollback first; re-merge master first).
2. Grid views task once question 1 is answered (replaces 002's fixed columns).
3. 070/071/072 after 038 and 043 owner inputs; 040 step 5 after 071.
4. Next PR to master carries `78e7b16d6` (A8 clarification); run `/conflict-check` + actionlint before it.
5. Before the SPA deploy: `user_impersonation` scope on the CIAM API app (task 041 Entra step).

## Dependencies tracked elsewhere
- ISS-001 (#1623), ISS-002 (#1633), ISS-003 (#1636), ISS-004, ISS-005 (#1673).
- Copilot agent still uses `access_as_user` on `1e40baad`.
- Operator: `mailbox-central@spaarke.com` + Exchange RBAC scoping for the stamp UAMI (H14a); BFF app user needs `prvDelete` on `sprk_servicerequest` (verify live).
