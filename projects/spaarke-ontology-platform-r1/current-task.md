# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-11 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (**D-106: critical path only**;
> merges D-107 branch + **D-115 master**) and §6 gotchas. Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-119).
> Issues for after the project: [`notes/defer-issues.md`](notes/defer-issues.md) (F-1..F-59 on the branch; 046 adds F-60/F-61, 031 adds F-62..F-64 — whichever merges second resolves the append conflict). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology lanes. 116 tasks: **69 ✅**, 1 🔄 (130: live proof in D-112 deploy), 33 🔲, 9 ⏸️, 4 🚫. |
| **Branch** | `docs/ontology-platform-design` (draft #1111) at `d4e947274`: master synced through `848f63913` (#1643) 2026-10-11; ArchTests guard re-pinned after #1640 (958/958). |
| **Owner prefs** | Finish fast but not at a cost premium. Keep 101/102/103/025; **073 lowest priority, last**. Subagent cap 4. Every Agent call passes `model`. Don't merge #1111 to master yet (recommendation given 2026-10-11; owner to choose: wait for 035/055, or add an on/off setting). |
| **Critical path** | **031 (merging)** → 032/033/034/025 → 035 deploy BFF → 050/038 → 059 → 055 deploy Console → 064. Decision path: 040 ✅ + **046 (merging)** → 043 → 058 → 045/049. Exit gate 074 ✅. |
| **Next Action** | When #1626 merges: mark 031 done (POML + TASK-INDEX + drift check), launch 032/033/034/025. When the 046 re-check is CLEAN and CI green: merge #1609 (`scratchpad/merge-when-green2.sh 1609 <full sha> docs/ontology-platform-design`), mark 046 done, launch **043** (POML carries D-119 + typed-pair constraints; tell uac-r2 when its PR is up). |

## Running now

| Lane | What | Then |
|---|---|---|
| **#1626 (031)** head `26057660…` | merge-when-green watcher into the project branch. The office-scope job may hit the 20-min cap (F-49): merge on log evidence + PR comment | Mark 031 done; resolve the defer-issues append conflict if 046 merged first |
| **#1609 (046)** head `e27104c85` | Scoped re-check (sonnet) of the test realignment to uac-r2 #1628/#1643 (4 tests re-asserted: pair-only/mismatched typed pair → 400; untyped invoice fallback → filed under the invoice). Results posted to uac-r2 (#1355 + SendMessage) | CLEAN + CI green → merge; F-findings → fix + re-check |

## Waiting / owner

- **Owner questions open:**
  - deferred-list walkthrough now or at project end;
  - rename defer rows F-nn → I-nn (avoid confusion with the F1–F4 triage classes);
  - confirm D-119 uses the communication's received date (not its classification date);
  - where to send the #1501 / dev-deploy answer;
  - #1111 merge strategy.
- **130 / 111 / 114**: run in the D-112 full E2E dev deploy.
- **Housekeeping (owner, by hand):** stale `C:\wt*` / `C:\wts-*` worktrees (list: `notes/handoff-history/2026-10.md`).
- **Checklist page** https://claude.ai/artifact/3FPj4oT83f25v2r8yutkMV.

## Coordination

uac-r2: post on #1355 **and** SendMessage `spaarke-wt-unified-access-control-r2-1d`. Their #1628 (pair must be repeated by a typed lookup) and #1643 (WA under invoice/event inherits) are on master and on our branch.
