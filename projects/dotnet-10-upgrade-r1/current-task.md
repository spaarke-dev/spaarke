# Current Task — dotnet-10-upgrade-r1

> **Purpose**: Active task state tracker for context recovery. Reset at each task transition (root CLAUDE.md §7).

---

## Active task

- **Task**: `001` — Bump global.json → 10.0.1xx SDK + re-scrape .NET 10 breaking changes (H5)
- **Status**: not-started
- **Phase**: P0 Retarget + build-green
- **POML**: [`tasks/001-bump-globaljson-sdk.poml`](tasks/001-bump-globaljson-sdk.poml)
- **Rigor**: FULL · **Model-tier**: sonnet · **Effort**: high

## Pipeline state

- **Planning artifacts**: ✅ generated 2026-08-11 (`plan.md`, 23 task POMLs, `TASK-INDEX.md`, this file).
- **Execution**: NOT started. `/project-pipeline` was run in **INITIALIZE-ONLY** mode (operator decision — plan artifacts only, then stop). Begin execution with `task-execute` on task 001 in a fresh session.
- **Branch**: `work/dotnet-10-upgrade-r1` (worktree already created; no branch-creation step needed).

## Steps completed this task

_(none — task not yet started)_

## Files modified this task

_(none yet)_

## Key decisions / notes

- This is a support-lifecycle retarget (net8 EOL 2026-11-10) with **zero product-behavior change** except the FR-06 telemetry carve-out.
- The retarget is intentionally a **serial atomic chain** (design §4 principle 2) — no P0 parallel groups.
- Deploy tasks (050/051/060/061) are **OPERATOR-DRIVEN** (Azure + go/no-go) — not autonomous.
- `net462` Dataverse plugin is **out of scope** (NFR-05).

## Next action

Run `task-execute` against `tasks/001-bump-globaljson-sdk.poml`.
