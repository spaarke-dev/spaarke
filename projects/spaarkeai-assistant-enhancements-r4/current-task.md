# Current Task — spaarkeai-assistant-enhancements-r4

> Tracks the **active task only**. History lives in `tasks/TASK-INDEX.md` and per-task `.poml` files.

---

## Active Task

**Task**: none (project initialized; execution owner-gated)

**Status**: not-started

**Next Action**: On owner go-ahead, begin **Phase 0 — task 001** (behavior-gap register + eval-case harness) via `task-execute`. Baseline is current (merged net10 master 2026-08-14).

**🆕 Runtime**: dev + this worktree are on **.NET 10** (`global.json` 10.0.100; BFF csproj `net10.0`; `dotnet build -c Release` verified clean). BFF builds/deploys need SDK ≥10.0.100; never deploy the BFF from a net8 tree. Re-baseline publish size fresh under net10.

---

## Session Notes

- Project initialized 2026-08-13 via `/project-pipeline` (INITIALIZE-ONLY — 17 tasks + planning artifacts; execution NOT started).
- Spec open questions resolved (owner 2026-08-13): E3 owned in R4 (redesign-r2 closed); advisory tier = ADR-016 Reasoning @ temp ~0.2–0.3.
- Baseline synced to `origin/master` (`033c43a91`) — pre-flight staleness cleared.

## Steps Completed (active task)

_(none — no active task)_

## Files Modified (active task)

_(none)_

## Decisions (active task)

_(none)_
