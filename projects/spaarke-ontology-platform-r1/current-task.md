# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-10 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (**D-106: critical path only**;
> merges D-107 branch + **D-115 master**) and §6 gotchas. Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-118).
> Issues for after the project: [`notes/defer-issues.md`](notes/defer-issues.md) (F-1..F-56; the 074 and 046 branches add their own F-rows: renumber on merge). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology lanes. 116 tasks: **68 ✅**, 1 🔄 (130: live proof in D-112 deploy), 34 🔲, 9 ⏸️, 4 🚫. 037 (#1603) and 040 (#1600) merged 2026-10-10. |
| **Branch** | `docs/ontology-platform-design` (draft #1111) at `459fc505d`. Synced with master 2026-10-10 (`48742f4d2`). |
| **Owner prefs (2026-10-10)** | Finish fast but not at a cost premium. Keep 101/102/103/025; **073 lowest priority, last**. Subagent cap 4 (owner may raise to 8). Every Agent call passes `model`. |
| **Critical path** | **031 (running)** → 032/033/034/025 → 035 deploy BFF → 050/038 → 059 → 055 deploy Console → 064. Decision path: 040 ✅ + 046 → 043 → 058 → 045/049. Exit gate 074. |
| **Next Action** | Act on lane notifications: PR → independent review → fix round → re-check → merge (`scratchpad/merge-when-green2.sh <pr> <head> <branch|master>`). When 046 merges start **043** (tell uac-r2 when its PR is up). |

## Running now

| Lane | What | Then |
|---|---|---|
| **031** (opus, worktree `C:\wts-031`, branch `task/ontology-031-evaluator`) | Evaluator via task-execute | Independent review (adversarial-reviewer, opus) → fix → re-check → merge; mark 031 done |
| **046 review 2** (adversarial-reviewer, opus) | Second pass on #1609 head d956779c3 (security-tagged: two passes). uac-r2 approved their parts; parity accepted (D-118) | Fix-now findings → lane fix + re-check; then merge; record "Reviewer: owner (D-118)" in parity notes; mark 046 done |
| **074 run 4** (sonnet) | Clean re-run in `C:\wts-d74r` (`tmp/074-run3-local`, unpushed). Run 3 = 81.8% (Wilson 69.7–89.8%) but INVALID: Azure token failures left some items without the live taxonomy; harness guard failed it | ≥ 80% and guard clean → review/merge #1585 (renumber F-rows) + #1602 (mirror, master); mark 074 done. Else → owner |
| **#1610 merge** (shell) | Rung-5 taxonomy (D-117 b); office-scope job timed out again (F-49), rerun 38072961261 | Merges into project branch when green |

## Waiting / owner

- **130 / 111 / 114**: run in the D-112 full E2E dev deploy.
- **Housekeeping (owner, by hand):** many stale `C:\wt*` / `C:\wts-*` worktrees (list: `notes/handoff-history/2026-10.md`). A runaway publish-size loop (`C:\wts-132-out`, unexpanded `$d`) was killed 2026-10-10.
- **Checklist page** https://claude.ai/artifact/3FPj4oT83f25v2r8yutkMV.

## Coordination

uac-r2: post on #1355 **and** SendMessage their session. They are fixing #1575 (F-52) and F-56 (secure-ness oracle in OwnedChildWrite, branch `fix/uac-r2-pair-appendto`). Email project owns the triage-email Action (#1584).
