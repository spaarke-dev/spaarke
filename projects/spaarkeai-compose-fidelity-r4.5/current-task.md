# Current Task — Spaarke Compose Legal Fidelity R4.5

> Tracks only the **active** task. History is in `tasks/TASK-INDEX.md` and per-task `.poml` files.

## Active Task

**Status**: in-progress (autonomous parallel execution)
**Mode**: autonomous, parallel-where-possible; `/conflict-check` before every BFF PR.

### Wave A (in progress)
- **001** corpus legal-numbering fixtures — 🔄 subagent (sonnet)
- **051** WS-5 Word-service eval + NFR-03 licensing — 🔄 subagent (opus)

### Wave plan
- Wave B (after 001): 002 harness ∥ 050 LibreOffice spike
- Then sequential BFF main line 010→011→012→013 (**run `/conflict-check` before 010**), 020→022, 030→033, 040→042; 052 parallel after 050+051.
- Deploy/UAT (014, 034) + fired escalation triggers → STOP for human.

## Project state

- Pipeline complete: README, spec, plan, CLAUDE.md, tasks/, TASK-INDEX.md generated 2026-07-28.
- Branch `work/spaarkeai-compose-fidelity-r4.5` — synced with origin, 0 behind master (R4 merged).
- Baseline BFF build: **green** (0 errors, 23 pre-existing warnings).

## Steps completed
_(none — no active task)_

## Files modified this task
_(none)_

## Decisions / deviations
_(none)_

## Reminders
- **`parallel-safe: false` on all `Services/Compose/` tasks** — run `/conflict-check` before every BFF PR.
- Consume `Services/Ai/PublicContracts/` — no fork of `Services/Ai/`.
- Publish size ≤60 MB; WS-1..WS-4 ~0 delta; WS-5 sidecar out-of-publish.
- Line numbers cited in spec/design may have shifted after today's master merge — re-grep before editing.
