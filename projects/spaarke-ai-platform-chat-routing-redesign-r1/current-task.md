# Current Task State — Spaarke AI Platform Chat Routing Redesign (R1)

> **Last Updated**: 2026-06-21 (by `/context-handoff` post-project-init commit, pre-overnight-autonomous-run)
> **Recovery**: Read "Quick Recovery" section first. After compact, the user wants **overnight autonomous execution starting at Wave 0-A0 (task 000)**.

---

## Quick Recovery (READ THIS FIRST — <30 seconds)

| Field | Value |
|-------|-------|
| **Project** | `spaarke-ai-platform-chat-routing-redesign-r1` |
| **Branch** | `work/spaarke-ai-platform-chat-routing-redesign-r1` (worktree at `c:\code_files\spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1\`) |
| **Origin status** | Pushed. Draft PR open: https://github.com/spaarke-dev/spaarke/pull/409 |
| **Last commit** | `c556702ee` — project init (137 files, +15,822 lines) |
| **Pipeline state** | `/project-pipeline` COMPLETE. Ready for `/task-execute` runs. |
| **Active task** | none — about to start Wave 0-A0 (task 000) |
| **Execution mode** | **OVERNIGHT AUTONOMOUS** (user explicitly requested 2026-06-21) |
| **Next Action** | **Spawn `task-execute` skill on task 000 (R6 readiness check). Then proceed wave-by-wave through TASK-INDEX following autonomous mode rules below.** |

### Critical context (3-sentence version)

The project is fully scaffolded and committed — 120 POML task files across 8 phases and ~55 execution waves are on origin/work-branch with draft PR #409 open. User wants overnight autonomous execution starting from Wave 0-A0 (task 000: R6 readiness check), continuing wave-by-wave through TASK-INDEX without prompts unless escalation criteria are hit. Phase 7 is **blocked** until R6 PR #401 merges to master — when reached, mark waves 7-A through 7-H as `🚧 blocked` and stop overnight execution at that point.

---

## OVERNIGHT AUTONOMOUS MODE — RULES OF ENGAGEMENT

This section defines how the post-compact agent should behave during unattended overnight execution.

### Authorization

The user has **explicitly authorized autonomous execution** for this project on 2026-06-21. No per-wave confirmation prompts. Proceed wave-by-wave through `tasks/TASK-INDEX.md` Critical Path until a STOP condition fires.

### Wave-by-wave execution loop

```
LOOP:
  1. Read TASK-INDEX.md → find next wave whose prereqs are all ✅
  2. Identify tasks in that wave + their parallel-safe flags
  3. IF wave has parallel-safe tasks:
       → Spawn task-execute skill once per task in a SINGLE message
         (parallel Skill tool calls; max 6 per CLAUDE.md hard limit)
  4. IF wave is sequential (parallel-safe: false):
       → Run task-execute on the single task; if multiple, run one at a time
  5. After ALL tasks in wave complete:
       a. Verify build between waves:
          - If any .cs modified → `dotnet build src/server/api/Sprk.Bff.Api/`
          - If any .ts/.tsx modified → `npm run build` in changed package
       b. Mark wave tasks ✅ in TASK-INDEX.md
       c. Update current-task.md (this file) with completed wave + next wave
       d. Commit ONE commit per wave with conventional message:
          `feat({wp-code})({phase-letter}): wave {wave} complete — {summary}`
       e. Push to origin (no PR ops — branch already pushed; PR #409 already open)
  6. Continue to next wave.

STOP conditions (see below).
```

### STOP conditions — halt execution and document state

Stop immediately and update current-task.md if ANY of these fire:

| # | Condition | Action |
|---|---|---|
| **S1** | Wave 7-A (task 140) reached AND R6 PR #401 not merged to master | Mark 7-A–7-H as 🚧 blocked. STOP. Log: "Phase 7 awaits R6 PR #401 merge — resume after merge." |
| **S2** | Any task fails the rigor-gate at task-execute Step 9.5 (code-review or adr-check FAIL) | Mark task 🔄 retry; STOP at end of current wave. Log root cause. |
| **S3** | Any wave fails build verification (`dotnet build` or `npm run build`) | Mark wave 🔄 retry; STOP. Log failing build output to `notes/handoffs/`. |
| **S4** | BFF publish-size exceeds 55 MB cumulative (NFR-01 escalation threshold per CLAUDE.md §10) | STOP. Log: "Publish-size escalation. Architecture review needed." |
| **S5** | BFF publish-size exceeds 60 MB (HARD ceiling per CLAUDE.md §10) | STOP IMMEDIATELY. Revert last wave's changes if needed. |
| **S6** | Ambiguous requirement encountered (per CLAUDE.md §6 — "must request human input for ambiguous or conflicting requirements") | Mark task 🚧 blocked. STOP. Log specifics in current-task.md "Blockers". |
| **S7** | ADR conflict or violation that task-execute cannot resolve automatically | Mark task 🚧 blocked. STOP. |
| **S8** | Breaking-change scenario (API contract, DB schema) NOT explicitly authorized by spec.md | Mark task 🚧 blocked. STOP. |
| **S9** | Context usage >70% during a wave | Run `context-handoff` immediately; STOP at end of current wave; request `/compact` on next run. |
| **S10** | A task explicitly tagged with `uat` or `e2e-test` requires manual user interaction (e.g., Power Apps UI navigation, dark-mode toggle) | Mark task 🚧 blocked, skip to next wave. Log: "Requires user interaction — defer to manual session." |
| **S11** | Cumulative wave-complete commits exceed 20 in this overnight run | STOP and report. Prevents runaway commit cascade if a bug causes false-positive completion signals. |

### Defensive defaults (continue, do not halt)

These conditions should NOT trigger a STOP — task-execute handles them:

- A single test failure inside a task (task-execute retries; if 3 retries fail, mark task 🔄, continue to next task in wave)
- A code-review WARNING (not CRITICAL) — log and continue
- A non-blocking adr-check finding (log and continue)
- A dependency timeout (`dotnet restore` slow) — retry once, then continue
- An informational warning from a linter — log and continue
- A merge conflict on `notes/handoffs/` (auto-resolve in favor of newer)

### Failure handling rhythm

| Outcome of a task | Action |
|---|---|
| ✅ green (passes all gates) | Mark ✅ in TASK-INDEX; continue |
| 🔄 transient fail (retry succeeded) | Mark ✅; note transient in commit msg |
| 🔄 persistent fail (3 retries, still failing) | Mark 🔄; skip to next task in wave; LOG; do NOT block siblings |
| 🚧 blocked (escalation criteria S2/S6/S7/S8) | Mark 🚧; STOP wave; STOP overnight run; update Blockers section |
| Failed wave (>50% of wave's tasks 🔄 or 🚧) | STOP overnight run; full report in current-task.md |

### Checkpointing rhythm

- After EVERY wave completes, update `current-task.md` "Wave Progress Tracker"
- After EVERY wave commits, push to origin
- After every 5 waves, run a full `context-handoff` (refresh this file's metadata)
- Never let context drift >70% — at 70%, run `context-handoff` + STOP

### Morning summary (when user returns)

When the human user returns, post a single concise summary in the next response with:

1. Total waves completed
2. Total tasks completed
3. Any 🔄 or 🚧 tasks (and root causes)
4. STOP condition that ended the run (or "ran to completion of Phase X")
5. Current branch state: commits ahead of origin, push status, PR #409 status
6. Recommended next action for the human

---

## Wave Progress Tracker

| Wave | Tasks | Status | Started | Completed | Commit SHA | Notes |
|---|---|---|---|---|---|---|
| **0-A0** | 000 | 🔲 pending | — | — | — | Next |
| **0-A** | 001, 002, 003 | 🔲 pending | — | — | — | |
| **0-B** | 004 | 🔲 pending | — | — | — | |
| **1-A** | 010, 011, 012 | 🔲 pending | — | — | — | |
| **1-B** | 013 | 🔲 pending | — | — | — | CRIT-1 fix: pre-extend WorkspaceOptions |
| **1-C** | 014 | 🔲 pending | — | — | — | Dataverse data update |
| **1-D** | 015 | 🔲 pending | — | — | — | FR-05 first migration |
| **1-E** | 016, 017, 018, 019 | 🔲 pending | — | — | — | 4 parallel Pattern A |
| **1-F → 7-H** | … | 🔲 pending | — | — | — | See TASK-INDEX |

*Update this table after each wave commits.*

---

## Execution authorization summary

| Authorization | Granted | Scope |
|---|---|---|
| Autonomous wave-by-wave execution | ✅ | All Phase 0–6 work (Phase 7 stops at 7-A pending R6 PR #401) |
| Parallel `task-execute` spawning (≤6 per wave) | ✅ | Per CLAUDE.md "Parallel Task Execution" rules |
| Build verification between waves | ✅ | Required |
| One commit per wave + push to origin | ✅ | Required |
| Modify TASK-INDEX, current-task.md, notes/handoffs/ | ✅ | These are the agent's working surface |
| Modify spec.md, design.md, architecture/, README.md, plan.md, CLAUDE.md | ❌ | Frozen at project-init — escalate to human if change needed |
| Modify ADRs (`.claude/adr/`, `docs/adr/`) | ❌ | Escalate. ADR-030 v2 already amended; no further ADR changes planned |
| Open/close/merge PRs | ❌ | PR #409 stays draft. No merge ops. |
| Force push, reset --hard, --no-verify | ❌ | Never. Halt if needed. |

---

## Pre-flight check (do this once at start of overnight run)

Before launching the first agent in Wave 0-A0:

```
1. Verify on correct branch + worktree:
   git branch --show-current  # → work/spaarke-ai-platform-chat-routing-redesign-r1
   git rev-parse --git-common-dir  # → C:/code_files/spaarke/.git (worktree confirmed)

2. Verify working tree clean:
   git status --porcelain  # → empty

3. Verify origin sync:
   git rev-list --count HEAD..origin/work/spaarke-ai-platform-chat-routing-redesign-r1  # → 0

4. Verify build baseline:
   dotnet build src/server/api/Sprk.Bff.Api/  # → exit 0

5. Verify PR #409 still draft + open:
   gh pr view 409 --json state,isDraft  # → {"isDraft":true,"state":"OPEN"}

6. If ALL pass → proceed with task-execute 000
7. If ANY fail → STOP, log in Blockers, do not auto-fix
```

---

## Next Action (explicit)

```
Invoke Skill task-execute on:
  projects/spaarke-ai-platform-chat-routing-redesign-r1/tasks/000-r6-closeout-readiness-check.poml

Wave: 0-A0 (single sequential task)
Rigor: MINIMAL
Expected duration: ~10–20 min

Outcome of 000:
  - IF R6 PR #401 merged + R6 UAT regression complete → mark 000 ✅, proceed to Wave 0-A (tasks 001, 002, 003 — parallel, 3 agents)
  - IF R6 NOT ready → mark 000 ✅ but note "Phase 7 blocked"; still proceed to Wave 0-A (Phases 0–6 don't depend on R6)
```

---

## Files Modified This Session (uncommitted)

**None.** Last commit `c556702ee` cleared the working tree.

Anything new from overnight execution will be tracked here by the post-compact agent.

---

## Pipeline Progress Tracker

| Step | Status | Notes |
|---|---|---|
| Plan mode setup | ✅ | Plan approved 2026-06-21 |
| Step 1.5 PR overlap check | ✅ | PR #401 blocks Phase 7; PR #406 minor |
| Step 2a Resource discovery | ✅ | 12 ADRs identified |
| Step 2b project-setup | ✅ | All scaffolding |
| Step 2b enrichment | ✅ | plan.md + CLAUDE.md enriched |
| Step 3 task-create | ✅ | 120/120 POMLs materialized via 6 parallel agents A–F |
| Audit pass | ✅ | CRIT-1–CRIT-8 fixes applied |
| Step 4 commit | ✅ | `c556702ee` |
| Step 4 push | ✅ | origin/work-branch |
| Step 4 PR | ✅ | Draft PR #409 open |
| Step 5 auto-task-execute | 🔲 | **About to start (overnight autonomous)** |

---

## Key Decisions Made (cumulative session log)

| Time | Decision | Rationale |
|---|---|---|
| Plan approval | Renumber cascade for Phase 1 (013–026 → 014–027 after inserting new 013) | User chose |
| Plan approval | Phase 7 reorder: code-review + adr-check BEFORE UAT | Standard quality-gate sequence |
| Audit | New tasks 000, 013, 070, 091 inserted | Resolve CRIT-1/3/4 + R6 readiness gate |
| Audit | Wave 4-A split into 4-A1 + 4-A2 | CRIT-2 sequential PaneEventTypes.ts edit |
| Audit | Cross-wave dep table added (098 needs 060; 103 needs 064) | CRIT-5 visibility |
| Audit | Wave 7-B made sequential atomic | CRIT-6 |
| Pipeline | 6-parallel-agent POML generation | Quality + speed |
| Validation | Wave 3-B (048-051) demoted to sequential | CRIT-8 file-overlap on PlaybookOutputHandler.cs |
| Push | Commit landed, draft PR #409 opened | User authorized |
| 2026-06-21 | **OVERNIGHT AUTONOMOUS authorization** | User explicit |

---

## Blockers

**Status**: None at this moment. Pre-overnight-run.

If overnight run produces blockers, append entries here with:
- Wave + task ID
- Stop condition that fired (S1–S11)
- Failing command output (if relevant)
- Recommended unblock action

---

## Quick Reference

### Project paths
- **Worktree**: `c:\code_files\spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1\`
- **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
- **PR**: https://github.com/spaarke-dev/spaarke/pull/409 (draft)
- **TASK-INDEX**: [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)
- **Architecture binding**: [`architecture/stateful-chat-architecture.md`](architecture/stateful-chat-architecture.md)
- **Spec**: [`spec.md`](spec.md) (45 FRs + 19 NFRs)
- **AI context loader**: [`CLAUDE.md`](CLAUDE.md)

### Applicable ADRs (this project)
ADR-001, ADR-008, ADR-010, ADR-013, ADR-014, ADR-015, ADR-018, ADR-019, ADR-029, **ADR-030 v2** (amended), ADR-032, ADR-033

### Critical guards (do not violate during overnight run)
- DO NOT modify spec.md, design.md, architecture/, README.md, plan.md, CLAUDE.md
- DO NOT modify `.claude/` (sub-agent write boundary; main session only — escalate to human if needed)
- DO NOT push to master
- DO NOT mark PR #409 ready-for-review (stays draft until human verifies)
- DO NOT use `--no-verify`, `--force`, or `git reset --hard`
- DO checkpoint after EVERY wave (`Wave Progress Tracker` table + commit + push)
- DO escalate any S1–S11 stop condition immediately

---

## Recovery Instructions (post-compact, post-/compact)

When the new session starts after `/compact`:

1. The user's first message will likely be "continue", "start overnight", "begin execution", or similar.
2. **Read this file's "Quick Recovery" section** (< 30 seconds).
3. **Read the "OVERNIGHT AUTONOMOUS MODE — RULES OF ENGAGEMENT" section** (full).
4. **Run pre-flight check** (the 6-step list above).
5. **Read `tasks/TASK-INDEX.md`** to confirm next wave + tasks + dependencies.
6. **Spawn `Skill: task-execute`** on task 000 (Wave 0-A0).
7. **Follow the wave-by-wave loop** — checkpoint each wave; commit each wave; push each wave.
8. **At any STOP condition**, halt, update Blockers, post a brief status, end the session.
9. **At end of overnight (whether by completion, stop, or context drift)**, post a morning summary per "Morning summary" template above.

---

*This file is the primary source of truth for active work state. Updated by `/context-handoff` 2026-06-21 to prepare for overnight autonomous execution.*
