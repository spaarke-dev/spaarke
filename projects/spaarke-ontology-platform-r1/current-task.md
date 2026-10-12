# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-11 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (**D-106: critical path only**;
> merges D-107 branch + **D-115 master**) and §6 gotchas. Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-120).
> Issues for after the project: [`notes/defer-issues.md`](notes/defer-issues.md) (F-1..F-66 on the branch; 034's branch adds a second "F-65" (#1661) to renumber when it merges; 025's adds F-67/F-68). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology lanes. 116 tasks: **73 ✅**, 1 🔄 (130: live proof in D-112 deploy), 29 🔲, 9 ⏸️, 4 🚫. |
| **Branch** | `docs/ontology-platform-design` (draft #1111): 031 (#1626), 046 (#1609), 033 (#1653), 032 (#1660 → `86c09c11d`) merged 2026-10-11. |
| **Owner prefs** | Finish fast but not at a cost premium. Keep 101/102/103/025; **073 lowest priority, last**. Subagent cap 4. Every Agent call passes `model`. Don't merge #1111 to master yet (owner to choose: wait for 035/055, or add an on/off setting). |
| **Critical path** | 031 ✅ → 032 ✅ / 033 ✅ / **034** / **025** → 035 deploy BFF → 050/038 → 059 → 055 deploy Console → 064. Decision path: 046 ✅ → **043** → 058 → 045/049. |
| **Next Action** | Wait for the three lane reports below; then for each: scoped re-check (sonnet) of the fix diff → CI green → `scratchpad/merge-when-green2.sh <pr> <full sha> docs/ontology-platform-design` → mark done (POML + TASK-INDEX + drift check). |

## Running now (background agents, worktrees `C:\wts-NNN`)

| Lane | PR | Doing | Then |
|---|---|---|---|
| **lane-034** (suppression) | #1654 | Merge project branch (032/033 in); uac-r2 F2: add status/window clause to the CALLER read in the count route + assert in the filter test; NIT-a comment at DecisionPlanEndpoints.cs:77-78; full ArchTests | Send uac-r2 the new head (they re-check `d6c0025a9..head`, #1355 + SendMessage); merge on their approval; renumber its defer row "F-65" (#1661) to the next free number |
| **lane-025** (threshold) | #1664 | Merge project branch; implement **D-120** (re-raise after a re-crossing: one-time marker when condition observed false after Acted; tests); inspect (do NOT re-run) the failed live seam test `SignalWriterSeamTests…RefreshDueDate(sprk_todo)` | Scoped re-check; owner told about the accidental live run; close #1662 on merge. If it needs a NEW column → owner decision |
| **lane-043** (commit route) | (PR # to come) | Merge project branch; fix its reviewer's 10 fix-now findings + K2 (core pair compare) + K4 (server-side plan-read time); EventIds 50360–69 | Scoped re-check; uac-r2 reviews its ledger entry |

## Waiting / owner

- **Owner questions open:** deferred-list walkthrough timing · rename defer rows F-nn → I-nn · D-119 date (received vs classification) · where to send the #1501/dev-deploy answer · #1111 merge strategy · #1659 association-confirmed call site (ask email project or defer) · #1658 hand-made budget revisions · #1661 (034 stamp vs secure wall) · 025's evaluator change vs spec FR-46 "no evaluator change" (amend FR-46 or record a decision) · threshold column list (matter only) · pre-existing invalid POMLs (092, 068, 131, 132, 134–138).
- **Reported to owner 2026-10-11:** lane 025 ran the live write-seam tests against spaarkedev1 by accident (cleanup verified by query); one failed (`…RefreshDueDate(subjectEntity: "sprk_todo")`), not re-run — lane inspecting by reading.
- **130 / 111 / 114**: run in the D-112 full E2E dev deploy.
- **Housekeeping (owner, by hand):** stale `C:\wt*` / `C:\wts-*` worktrees (list: `notes/handoff-history/2026-10.md`).
- **Checklist page** https://claude.ai/artifact/3FPj4oT83f25v2r8yutkMV.

## Coordination

uac-r2: post on #1355 **and** SendMessage `spaarke-wt-unified-access-control-r2-1d`. F-49: the office-scope CI job can hang to its 20-min cap — rerun failed jobs.
