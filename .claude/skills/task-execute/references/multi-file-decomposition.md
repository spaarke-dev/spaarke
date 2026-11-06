# Multi-file work decomposition — Step 8.0 procedure

Read this at Step 8.0 when the task touches 4+ files in independent modules and the per-module work is substantial, and you are deciding whether to delegate. When you finish, return to `SKILL.md` Step 8.

```
BEFORE starting implementation steps, ANALYZE the work:

IF task touches 4+ files in INDEPENDENT modules AND the per-module work is substantial:
  1. DECOMPOSE into independent sub-tasks:
     - Group files by module/component
     - Identify which changes depend on others
     - Create dependency graph

  2. IDENTIFY parallel-safe work:
     - Files in different modules → CAN parallelize
     - Files with no shared interfaces → CAN parallelize
     - Files where one imports from another → MUST serialize

  3. DELEGATE to subagents:
     - Use the **Agent tool** with subagent_type="implementer" (Sonnet/high), or pass `model` chosen per `.claude/constraints/agent-cost.md` — a launch with no model is refused by the `PreToolUse` hook
     - Send ONE message with MULTIPLE Agent tool calls for parallel work
     - Each subagent handles one module/component
     - Provide each subagent with:
       • Specific files to modify
       • Relevant constraints from task POML
       • Expected changes (from task steps)
     - Put `task <NNN>` in each Agent `description` (Step 10 rework line)
     - NOTE: subagents CANNOT write to `.claude/` (sub-agent write boundary, root CLAUDE.md §3) — keep any
       `.claude/`-touching work in the main session.

  4. COORDINATE results:
     - You are notified when each background Agent completes (no polling tool needed)
     - Merge changes if needed
     - Run integration checks after all subagents complete

EXAMPLE - Task modifies 6 files:
  Files: EndpointA.cs, EndpointB.cs, EndpointC.cs (API)
         ComponentA.tsx, ComponentB.tsx (PCF)
         SharedTypes.ts (shared)

  Dependency analysis:
    SharedTypes.ts → ComponentA.tsx, ComponentB.tsx (shared must complete first)
    EndpointA.cs, EndpointB.cs, EndpointC.cs (all independent)

  Execution strategy:
    Phase 1 (serial): SharedTypes.ts
    Phase 2 (parallel):
      - Subagent 1: EndpointA.cs, EndpointB.cs, EndpointC.cs
      - Subagent 2: ComponentA.tsx
      - Subagent 3: ComponentB.tsx

  Agent tool call (Phase 2):
    Send ONE message with THREE Agent tool calls:
    - Agent 1: "Implement API endpoints A, B, C"
    - Agent 2: "Update ComponentA with new types"
    - Agent 3: "Update ComponentB with new types"

WHEN NOT to parallelize:
  - Task has only 1-3 files (overhead not worth it)
  - Files have tight coupling (shared state, imports)
  - Sequential logic required (create then use)
  - Context has been reported high (checkpoint first; subagents return results into this context)
```

Delegated work is still this task's work: Steps 9 and 9.5 run over every file the subagents changed.
