# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-10 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (**D-106: critical path only**;
> merges D-107 branch + **D-115 master**) and §6 gotchas. Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-116).
> Issues for after the project: [`notes/defer-issues.md`](notes/defer-issues.md) (F-1..F-52; F-53 on #1585). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology lanes. 116 tasks: **66 ✅**, 1 🔄 (130: live proof in D-112 deploy), 36 🔲, 9 ⏸️, 4 🚫. |
| **Branch** | `docs/ontology-platform-design` (draft #1111), synced with master 2026-10-10 (`48742f4d2`; includes #1555 048 move). ArchTests 958/958 on a clean run (BFF-boot tests flake under load). |
| **Owner prefs (2026-10-10)** | Finish fast but not at a cost premium: parallel lanes are fine (same total cost). Keep 101/102/103/025 in scope; **073 memo source lowest priority, schedule last**. Owner may raise `CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS` to 8 (currently 4). Every Agent call must pass `model` (PreToolUse hook; `.claude/constraints/agent-cost.md`). |
| **Critical path** | 037 → 031 evaluator → 032/033/034/025 → 035 deploy BFF → 050/038 → 059 → 055 deploy Console → 064. Decision path: 040 + 046 → 043 → 058 → 045/049. Exit gate 074. |
| **Next Action** | Act on lane notifications: PR → independent review (D-106) → fix round → re-check → merge (D-107/D-115, use `scratchpad/merge-when-green2.sh`). When 037 merges start **031** (opus). When 040 + 046 merge start **043**. |

## Running now (SendMessage resumes an agent by id)

| Lane | What | Then |
|---|---|---|
| **040** `af0c746c20898691d` | PR #1600 (head f3362fb8d). Review found F4 (create failure not a refusal), F3 (no live seam pairing), F2 (oversize memo after emails), F3 (log/metric unasserted) → fix round running | Re-check fix diff → uac-r2 review of the `RecordOwnerAssignmentCensusTests.cs` PerUser entry → merge |
| **037** `a1e20f46f6de7bcfb` | Signal writer Do-lane subjects; full suite running | Review → merge → **start 031** |
| **046** `a3f3afc52e51183da` (opus) | WA create via `OwnedChildWrite.CreateAsync` + `child-records` route (D-113); parity live proof | Review; uac-r2 review (their files); WA area owner parity review → merge |
| **074** `a1bcb0bae3c363ca9` | Run 1 FAILED 21.8% (prompt never rendered: row corrupted 2026-09-29 — repaired 13:33Z, owner-approved — AND example inputs are objects since July). Owner approved fix #2: mirror PR to master + row write + re-run once | Review/merge mirror PR (D-115); PR #1585 (fixture/harness/results) review → merge; ≥ 80% closes 074, else escalate |

## Waiting / owner

- **130 / 111 / 114**: run in the D-112 full E2E dev deploy.
- **Housekeeping (owner, by hand):** `C:\wt120d`, `C:\wtr1386`, `C:\wtrv1460`, `C:\wtrv1480*`, `C:\wts-b56`, `C:\wts-135`, `C:\wts-131`; older list in `notes/handoff-history/2026-10.md`. (039 SPE containers: owner deleted 2026-10-10.)
- **Checklist page** https://claude.ai/artifact/3FPj4oT83f25v2r8yutkMV: r2-r5 → 111; r6/r7/r8 → deferred 121/122/129.

## Coordination

uac-r2: post on #1355 **and** SendMessage their session (`spaarke-wt-unified-access-control-r2-*`). They are fixing #1575 (scheduler tick drop, F-52). Email project owns the triage-email Action (#1584).
