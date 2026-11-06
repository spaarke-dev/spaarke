# `/goal` wave-completion loop

Read this when the operator (or `project-pipeline` Step 5) has run `/goal` for the current wave, or when an `<escalation><trigger>` fires while such a loop is running. It does not apply to ordinary task-by-task execution.

`/goal` (Claude Code v2.1.139+) is an **optional per-wave** loop: the session keeps taking turns until a **transcript-only** evaluator (a small model by default — it reads the conversation and runs no commands or file reads) judges the wave's compiled condition met. It replaces the operator's per-task "continue" press. It is set for waves that `task-create` Step 3.85 marked `goal-eligible: YES`. **It is not a per-task mechanism and never a quality gate.**

**When it applies:** only for `goal-eligible` waves (machine-verifiable end-state, ≥3 well-specified tasks, low ambiguity, not security/deploy/irreversible). Non-eligible waves run task-by-task as normal — do not wrap them in `/goal`.

**Prerequisites** (if unavailable, `/goal` reports why — fall back to manual per-task execution): workspace trust accepted; hooks not disabled (`disableAllHooks` unset, no `allowManagedHooksOnly` in managed settings); Claude Code v2.1.139+.

**Mechanics:**
- The operator runs `/goal "<goal-condition>"` (the compiled, by-reference, ≤4,000-char condition stored in TASK-INDEX.md) AFTER context loading, so knowledge/constraints are in the transcript before the loop begins.
- Each task in the wave STILL runs its own task-execute with full context loading and **Step 9.5 gates unchanged**. The loop only removes the between-task human prompt.
- Unattended runs pair `/goal` with auto-mode (`claude -p "/goal <condition>"` + tool auto-approval) — appropriate **only** when the wave's constraints and outputs are fully declared. Interactive is the default.

**Three exit states:**
1. **Condition met** → proceed to the normal completion flow. Goal achievement is *necessary but not sufficient*: the orchestrator's accept / patch / escalate / block decision + Step 9.5 remain the actual gate for marking tasks `complete`. **Do NOT auto-mark tasks complete because the goal cleared.**
2. **`BLOCKED.md` written** (an `<escalation>` trigger fired, or a genuine blocker) → the wave's affected task moves to `blocked`; `BLOCKED.md` contents flow to the orchestrator per root CLAUDE.md §6 / §6.5. Writing `BLOCKED.md` is the *correct* behavior inside a loop, not a failure. `BLOCKED.md` (under `projects/{name}/`) states: task id, blocking condition, what was attempted, what decision is needed.
3. **Turn/time cap hit** → treated as an implicit block: write `BLOCKED.md` summarizing state + why the cap was reached, then the same escalation path applies.

**Non-negotiable:** the evaluator's judgment is a stopping-condition check, not a verdict on quality. Do not weaken, skip, or reorder Step 9.5 on the grounds that the goal was achieved.
