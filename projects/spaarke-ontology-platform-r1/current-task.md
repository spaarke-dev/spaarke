# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-12 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (**D-106: critical path only**;
> merges D-107 branch + **D-115 master**) and §6 gotchas. Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-120).
> Deferred work: one self-contained GitHub issue per item, label `ontology-deferred` (index in
> [`notes/defer-issues.md`](notes/defer-issues.md) "Pickup index"). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology lanes. **74 ✅**, 1 🔄 (130: live proof in D-112 deploy), 28 🔲, 9 ⏸️, 4 🚫. |
| **Branch** | `docs/ontology-platform-design` (draft #1111). Merged in: 031, 046, 033, 032 (2026-10-11), 034 (#1654 → `02ba5f29b`, 2026-10-12). |
| **Owner prefs** | Finish fast but not at a cost premium. Keep 101/102/103/025; **073 lowest priority, last**. Subagent cap 4. Every Agent call passes `model`. Don't merge #1111 to master yet (owner Q8). |
| **Critical path** | 031 ✅ 032 ✅ 033 ✅ 034 ✅ / **025** → 035 deploy BFF → 050/038 → 059 → 055 deploy Console → 064. Decision path: 046 ✅ → **043** → 058 → 045/049. |
| **Next Action** | Wait for (1) our scoped re-check of 043 fix commit `0ebd6ffcb` and (2) uac-r2's re-check comment on #1665. **Keep #1665 head at `5e431c2e4` until uac-r2's comment lands** (their request). Then: fix any F-class → merge when green + owner Q13–Q19/K5 answered → mark 043 done. |

## Open PRs (lanes finished; worktrees `C:\wts-NNN` kept)

| Task | PR | Head | State | To merge |
|---|---|---|---|---|
| **043** decision commit route | #1665 | `5e431c2e4136ba182657a9c51297e1fde898cc54` (fixes `0ebd6ffcb`) | Both reviews' findings fixed; tests 1670/1670, ArchTests 958/958. Re-checks running (ours + uac-r2, which probes F1 variants and lease TTL). uac-r2's K2 is theirs (#1730): keep 403 `caller_unresolved`. | Re-checks clean, CI green, owner Q13–Q19 + K5 replay behaviour. Q18 "send-reminder non-resolving" is a code change if chosen. |
| **025** threshold signals | #1664 | `d638d331e` | D-120 implemented, 034 merged in; re-checks clean. | Owner Q2 (amend FR-46). If A → record D-121. On merge close #1662, mark 025 done. |

Merge: `scratchpad/merge-when-green2.sh <pr> <full sha> docs/ontology-platform-design` (`gh pr ready` first if draft). Office-scope CI job may hit its 20-min cap (F-49, #1718): merge on log evidence with a PR comment.

## Waiting / owner

- **Owner decisions page** https://claude.ai/artifact/ADCv42eKukX2BQx7d62Mop — Q1–Q19 open (Q1 D-119 date, Q2 FR-46, Q3 threshold columns, Q4 #1661, Q5 #1658, Q6 #1659, Q7 re-run failed live seam test, Q8 #1111 merge timing, Q9 #1501 answer, Q10 walkthrough, Q11 F-nn→I-nn, Q12 invalid POMLs, Q13–Q19 043 behaviours) + 043's K5 replay behaviour (unauthorized Signal on replay → 200 Replayed, Signal `Unchanged`, warning `signal_close_not_authorized`).
- **After ontology works:** add the close-out phase (deferred issues batched by area) to TASK-INDEX (owner agreed in principle).
- **130 / 111 / 114**: run in the D-112 full E2E dev deploy.
- **Housekeeping (owner, by hand):** stale `C:\wt*` / `C:\wts-*` worktrees (list: `notes/handoff-history/2026-10.md`).

## Coordination

uac-r2: post on #1355 **and** SendMessage `spaarke-wt-unified-access-control-r2-1d`. EventIds 50360–50364 belong to 043.
