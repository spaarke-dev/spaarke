# Parallel waves — Step 0.3 procedure

Read this when `task-execute` is invoked for **more than one task at once** (e.g. "work on Phase 2 tasks", or from `project-pipeline` Step 5). For a single task, Step 0.3 does not apply. When you finish here, return to `SKILL.md` Step 0.5 — each task in a wave still runs the full protocol (Steps 0.5–11) in its own agent.

```
IF invoked for multiple tasks:
  1. READ TASK-INDEX.md "Parallel Execution Groups" section
  2. IDENTIFY current wave (group whose prerequisites are satisfied)
  3. COUNT parallel-safe tasks in this wave

  IF wave has >1 parallel-safe task:
    → ENTER PARALLEL MODE:
      a. Cap concurrency at 6 agents (API-overload guard; raise only with evidence — project-pipeline Step 5)
      b. Spawn N sub-agents (one per task) using Agent tool
         - ONE message with MULTIPLE Agent tool calls (critical — must be parallel, not sequential)
         - Each agent runs task-execute for its own task
         - Each agent gets its task POML file path + minimal context
         - Each launch states model + effort from the POML <model-tier> / <effort>
           (.claude/constraints/agent-cost.md; a launch with no model is refused by the PreToolUse hook)
      c. Wait for ALL agents to complete (background: true, notified on complete)
      d. Aggregate results (success/failure per task)
      e. **MANDATORY BUILD VERIFICATION**:
         - If any wave task modified `.cs` files: `dotnet build src/server/api/Sprk.Bff.Api/`
         - If any wave task modified `.ts`/`.tsx`: **`npm run build:prod` for PCF, run through `scripts/Invoke-PcfBuildProd.ps1`** (NOT `npm run build`, per root CLAUDE.md §12 / FAILURE-MODES AP-1; and `pcf-scripts` exits 0 on a failed build, so judge the result from the output); `npm run build` for non-PCF packages without a `:prod` script
         - If build fails: STOP. Report breakage. Do not start next wave.
      f. Update TASK-INDEX.md statuses (🔲 → ✅ for success, 🔄 for retry)
      g. Proceed to next wave

  IF wave has any task marked `<parallel-safe>false</parallel-safe>`:
    → Execute those tasks SEQUENTIALLY from main session
    → Reason for sequential: check <parallel-reason> metadata
    → Common reason: "touches .claude/ — main-session-only (permission boundary)"

  IF the user or harness has reported that context is high, or a compaction notice has appeared:
    → Checkpoint first (context-handoff skill)
    → Then enter parallel mode (each sub-agent gets fresh context window)
  (Claude cannot measure its own context usage — root CLAUDE.md §5 — so no step here keys on a percentage.)

FAILURE ISOLATION:
  - One agent failing does NOT abort the wave
  - Collect all agent outcomes (success/failure/timeout)
  - Report: "Wave X complete: {N} succeeded, {M} failed, {K} need retry"
  - Mark failed tasks 🔄 (not ❌) — they may succeed on retry

PERMISSION BOUNDARY REMINDER:
  - Sub-agents CANNOT write to `.claude/` paths
  - Tasks touching `.claude/` are auto-marked `parallel-safe: false` by task-create
  - If an agent is accidentally dispatched to a `.claude/` task, it will fail with "Edit denied"
  - This is EXPECTED behavior — main session picks up the task and runs sequentially
  - See root CLAUDE.md §3 "Sub-agent write boundary"

ELSE (single task invocation):
  → Skip to Step 0.5
```

If the operator has set `/goal` for this wave, also read `goal-wave-loop.md` (this folder) before dispatching.
