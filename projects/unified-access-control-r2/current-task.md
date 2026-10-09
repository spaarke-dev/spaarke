# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–89) and `notes/decisions.md`. Live records: `notes/batch5-live-gates-2026-10-08.md`. Owner checks: `notes/owner-hands-on-checklist-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-09 ~15:45 local (checkpoint #26, before /compact).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Session 28, merge and deploy of 3 verified PRs. **177** #1517 is MERGED (`f3d0a498e`) and its BFF is DEPLOYED to dev (healthz 200). The leftover job from the previous session did it. Still to merge: **180** #1519 (master conflict resolved `eb71d996f`, CI re-running), **175** #1503 (master conflict resolved `2f07a22b4`, CI running), **176** #1520 (clean vs master, CI running). **No merge job is running now.** All leftover/old jobs were killed by PID. |
| **Next Action** | (1) Launch the ONE sequential job, in the BACKGROUND: `bash "C:/Users/RALPHS~1/AppData/Local/Temp/claude/c--code-files-spaarke-wt-unified-access-control-r2/993ea642-9455-4e0e-8f57-ed99084c5e37/scratchpad/orchestrate.sh"`. It polls every 90 s, then: merge #1519 → 180 ribbon `-Apply` (`Set-CreatePrivilegeRibbon.ps1`, which also hides the "New Document" appaction) → merge #1503 → merge #1520 → ONE deploy from C:\wtR2: schema `-Verify` (pre-BFF), BFF, 175 web resources (`sprk_/scripts/access_ribbon.js`, `sprk_accesspermission_inherited`) + readback, 175 form lock (-SelfTest/dry/-Apply/-Verify), access ribbon `-Verify`, TrackingFieldTrio 1.0.45 import. It verifies each merge really happened and tolerates only a timeout-cancelled "Server tests (office scope)" (#1474). (2) If a merge is refused, it is usually a new master conflict: `git merge-tree --write-tree --name-only origin/master HEAD` in that worktree, resolve, push, then re-run the job. Remove already-merged PRs from the script first. (3) After the deploy: watch 2 `secure-root-inheritance` runs (`followParents`: notCompleted 0, no access_record_hidden/not_secured); re-run the schema `-Verify` (read probe); then the 175 live gate. If the access ribbon `-Verify` fails, run `Set-AccessRibbon.ps1 -Apply`. (4) The 180 live gate G180-2: a read-only role sees no custom Create buttons; Basic User still does. (5) 177 post-deploy: one-off Send-to-Index for the 141 relocated dev docs (query in the PR #1517 body). (6) Close 177 now (merged + deployed; its live check is the Send-to-Index repair), then close 175/176/180 after their gates: POML status + TASK-INDEX row + `scripts/check-task-status-drift.ps1` (9 known false positives) + board sync. (7) Start 179 and 181 (both after 175), then 178 (design note → owner first), then 136 → 099 → 036 → 090. |

## Rules learned this session (also in project CLAUDE.md §6 where durable)
- **ONE background polling job at a time.** Four parallel merge jobs exhausted the page file (Win32 1455) and died. A job started in a previous session can SURVIVE the restart and still act: check `ps -ef | grep merge` before launching.
- **Never launch a merge job with `&`** (no log). Always use `run_in_background`.
- **Subagents do not survive a session restart.** Spawn new ones with full briefs. The worktrees keep the work: C:\wt175, C:\wt176, C:\wt180.
- Master moves fast (other projects). Re-check each PR for conflicts with `git merge-tree` right before merging; a conflicting PR gets no CI.
- A newly created Dataverse column needs a table publish before `fieldpermissions` can reference it (175's script now waits).

## Dev state
- **BFF:** master `f3d0a498e` (includes 174 and 177).
- **175 schema:** `sprk_accessinheritance` on work assignments and projects, field-secured, writer profile only; `-Verify` PASS.
- **Owner-facing UI already on dev:**
  - TrackingFieldTrio 1.0.43 (174);
  - banner 1.0.1 (#1490);
  - 173 form lock;
  - 153 banner.
- **Banner still shows "Access status unavailable"** for the owner. The server side is verified fine. Waiting on the owner's browser details: Network tab filtered `no-access|config/client` after Ctrl+Shift+R, plus `[Access Status]` / `[BffAuth]` console lines.

## Owner decisions this session (recorded in notes)
- **R87:** the parent sets a FLOOR; a child may be stricter; only inherited values follow down; a re-file never loosens; the lock covers Secure and Access Permission only.
- **R88:** one SPE container per secure family; un-secure moves files to the business unit → task 178.
- **R89:** Manage Access needs the Share privilege → 179; Create buttons follow the Create privilege → 180; internal grant notification → 181.
- **180:** hide the modern "New Document" appaction (owner OK).
- **Main-session calls:**
  - 176: option B batch read as the caller; SearchDocuments unbound and trimmed; Precedents need every supporting matter readable; `/api/ai/search` per-document trim.
  - 177: the index parent is the governing record (one secure → it; none → most specific; two secure roots or unreadable → none).
- **Open policy question:** curated Precedents as firm-wide knowledge (option c), for later.

## Task status
- **Done:** 125 (174 closed).
- **Merged + deployed, closable:** 177.
- **Verified, merging:** 175, 176, 180.
- **Waiting on owner checks:** 067 (§6), 153 (§7, blocked by the banner), 173 (§8), 154, 114, 171, batch 4 (137, 140, 142, 143, 147, 150, 157, 162–166, 168, 169).
- **Not started:** 179, 181 (after 175); 178 (after 175, design first); 136; 099; 036; 090. Bookkeeping: 013/037/039 (with 136), 058 (090), 066 (067).

## Open owner items
- The banner browser details (above).
- No Access Entries in the app navigation?
- Checklist §1–§8.
- Run `Remove-TestContainers.ps1`.
- CIAM identities for 140; users for 171; 165 role grant.
- Set the Share privilege on roles after 179.
- Authorize the claude.ai and legal-plugin connectors if wanted.

## Issues filed this session
- #1489 KPI/rollup `/api/api`
- #1492 SpaarkeMaster source missing web resources
- #1514 per-document check cost
- #1516 bulk indexing sweep broken + "Unknown Matter" labels
- #1521 ribbon source drift
- #1522 index-name resolver short names
- #1533 search counts include unreadable docs
- #1534 flaky ContentSafety test
- Plus earlier: #1474, #1478, #1482, #1449, #1435–#1445.

## Worktrees
- **Active:** C:\wt175, C:\wt176, C:\wt180 (C:\wt177 can be removed).
- **Deploy:** C:\wtR2 (clean, at `f3d0a498e`).
- **Removable:** C:\wt174, C:\wt177, C:\wt1488, C:\wt064, C:\wt067, C:\wt113, C:\wt153, C:\wt173, C:\wt1410, C:\wt154, C:\wt105, C:\wt101, C:\wt114u. Run `cmd /c rmdir` on node_modules junctions first.
