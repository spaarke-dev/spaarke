# task-execute — worked examples

Illustrations only; the binding protocol is in `SKILL.md`. Task ids and paths are examples.

## Example: executing a task

**User**: "Execute task 013"

**Agent**:
```
0.5 DECLARE rigor (tags pcf/react → FULL), model tier, step mode
0.  CHECK current-task.md for recovery state (size guard, registration guard)
1.  READ tasks/013-update-aisummarypanel.poml
2.  UPDATE current-task.md: task 013, status: in-progress
3.  EXTRACT tags: pcf, react, fluent-ui, frontend
4.  LOAD KNOWLEDGE:
    - READ src/client/pcf/CLAUDE.md
    - READ docs/guides/PCF-DEPLOYMENT-GUIDE.md
    - READ references/surface-checklists.md (PCF section)
    - UPDATE current-task.md with knowledge files loaded
5.  LOAD CONSTRAINTS:
    - ADR-006, ADR-021: PCF patterns
    - UPDATE current-task.md with applicable ADRs
6.  LOAD ALWAYS-APPLY:
    - adr-aware skill
    - spaarke-conventions skill
    - script-aware skill
8.  EXECUTE STEPS 1-8 from task:
    - UPDATE current-task.md after each step
    - Track files modified
    - Log decisions made
    - Checkpoint per Step 8.5
9.  VERIFY acceptance criteria (+ PCF checklist)
9.5 RUN code-review + adr-check; triage and fix
9.6 RUN /devops-project-sync
10. UPDATE task status to completed; run check-task-status-drift.ps1
11. UPDATE current-task.md: reset, set next task
```

## Example: checkpoint flow (Step 8.5)

```
[Completing step 3 of 8]

Claude:
  1. Update current-task.md Completed Steps
  2. Check: Step 3 complete → checkpoint trigger
  3. Run context-handoff (Quick Recovery update)
  4. Report: "✅ Checkpoint saved. Continuing with step 4..."
  5. Begin step 4

[After step 6, which deployed to dev]

Claude:
  1. Update current-task.md Completed Steps
  2. Check: Step 6 complete → checkpoint trigger
  3. Check: a deployment happened → verbose checkpoint
  4. Run context-handoff
  5. Report:
     "✅ Checkpoint saved.
      Task: 013 - Add dark mode, Step 6 of 8 (deployed to dev)
      Continuing with step 7..."
  6. Begin step 7
```

## Example: recovery after a new session (Step 0)

**User**: "Where was I?" (after new session)

**Agent**:
```
1. READ projects/ai-doc-summary/current-task.md
2. FOUND: Task 013, status: in-progress, step 5 of 8 complete
3. LOAD tasks/013-update-aisummarypanel.poml
4. LOAD knowledge files, constraints, ADRs per task (Steps 4–7)
5. REPORT:
   "✅ Recovered context for ai-doc-summary

   Task: 013 - Update AISummaryPanel component
   Completed: Steps 1-5
   Files modified: src/client/pcf/AISummaryPanel/...

   Next: Step 6 - Add dark mode support

   Ready to continue?"
```
