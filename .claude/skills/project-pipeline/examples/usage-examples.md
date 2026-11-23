# project-pipeline — usage examples

Illustrative runs. Step numbers match `SKILL.md`; counts and PR numbers are illustrative.

## Contents

- Example 1: Autonomous flow (default)
- Example 2: Interactive flow (explicit request)
- Example 3: Review and refine before proceeding (interactive)
- Example 4: Stop and continue manually
- Example 5: Parallel wave flow (Step 5)

## Example 1: Autonomous flow (default)

```
User: "start project ai-document-intelligence-r1"

Agent: ✅ Step 0.3: Pre-flight passed (clean tree, 0 behind, build passing)
       ✅ Step 0.5: Master is current — all branch work merged.
       ✅ Step 1: SPEC.md validated (2,306 words)
       ✅ Step 1.5: No active PR overlaps detected.
       ✅ Step 1.7: ADR tensions: none declared
       ✅ Step 2: Resources discovered (4 ADRs, 2 skills, 3 knowledge docs)
                  Artifacts generated (README, plan, CLAUDE.md)
       ✅ Step 3: 178 tasks created, 6 parallel groups identified, Completeness Lint passed
       ✅ Step 4: Committed on work/ai-document-intelligence-r1, pushed, draft PR #265 opened
       ✅ Step 4.2: Registered: Project Issue #N under Epic #E · Board #2 · label ai-document-intelligence-r1
       🚀 Step 5: Starting task execution...

       Executing Group 0: Task 001 (serial — foundation setup)
       ✅ Task 001 complete.

       Executing Group A: Tasks 010, 011, 012 (parallel — 3 agents)
       ✅ All 3 tasks complete. Build verified.

       Executing Group B: Tasks 020, 021 (parallel — 2 agents)
       [continues autonomously...]
```

## Example 2: Interactive flow (explicit request)

```
User: "start project interactively"

Agent: ✅ SPEC.md validated (2,306 words)
       [Y to proceed / refine / stop]

User: "y"
[... a confirmation gate after each step — references/interactive-mode.md ...]
```

## Example 3: Review and refine before proceeding (interactive)

```
User: "initialize project from projects/new-feature/ with approvals"

Agent: ✅ SPEC.md validated
       [Y to proceed]

User: "review"

Agent: [Opens SPEC.md for review]
       Ready to generate the artifacts? [Y to proceed]

User: "y"

Agent: ✅ plan.md generated
       [Y to proceed / review to view plan]

User: "review"

Agent: [Opens plan.md]
       plan.md looks good? [Y to proceed / refine {section}]

User: "refine phase 2 - break down UI tasks more"

Agent: [Updates Phase 2 with more granular deliverables]
       ✅ Phase 2 refined
       [Y to proceed]

User: "y"

Agent: ✅ Tasks generated
       [Y to continue to branch creation]
```

## Example 4: Stop and continue manually

```
User: "start project new-api-feature"   (interactive)

Agent: ✅ SPEC.md validated
       [Y to proceed]

User: "y"

Agent: ✅ plan.md generated
       [Y to proceed]

User: "stop"

Agent: ✅ Project initialized up to plan.md

       Files created:
       - projects/new-api-feature/plan.md
       - projects/new-api-feature/README.md

       When ready to continue:
       - Run: /task-create new-api-feature
       - Or: /project-pipeline new-api-feature (restart the full pipeline)
```

## Example 5: Parallel wave flow (Step 5)

```
Group A (tasks 010, 011, 012) — prerequisite: 001 ✅
  → spawn 3 agents in one message (model = each POML's <model-tier>)
  → all complete → build verified → TASK-INDEX updated
Group B (tasks 020, 021) — prerequisite: 012 ✅
  → spawn 2 agents in one message
  → 020 succeeded, 021 failed (cause named: missing config key; supplied) → 021 retried sequentially
  → continue...
```
