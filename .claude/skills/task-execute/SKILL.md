---
description: Execute a POML task file with proper context loading and verification
tags: [tasks, execution, context, knowledge]
techStack: [all]
appliesTo: ["execute task", "run task", "start task", "work on task"]
alwaysApply: false
---

# task-execute

> **Category**: Project Lifecycle
> **Last Updated**: December 2025

---

## Purpose

Execute a single POML task file with **mandatory context loading** and **persistent state tracking**. This skill ensures Claude Code reads all required knowledge files, follows ADRs, applies skills before implementation, and maintains recoverable state across compaction.

**Critical**: This skill prevents the common failure mode where Claude Code starts implementing without loading the knowledge files referenced in the task.

**Context Persistence**: All progress is tracked in `current-task.md` so work can continue after compaction or new sessions.

---

## When to Use

- User says "execute task 013" or "work on task 013"
- User provides a task file path
- Continuing work on a project task
- Resuming after compaction or new session
- **Always use this protocol** when working on any POML task

---

## Execution Protocol (MANDATORY STEPS)

### Step 0: Context Recovery Check

```
IF resuming work (not fresh start):
  READ projects/{project-name}/current-task.md

  IF current-task.md exists AND status == "in-progress":
    → This is a continuation
    → EXTRACT: completed_steps, files_modified, next_step
    → SKIP to the step indicated in next_step
    → REPORT: "Resuming task {id} from step {N}"

  IF current-task.md shows different task:
    → WARN user: "current-task.md shows task {X}, you requested task {Y}"
    → ASK: "Switch to task {Y}? This will update current-task.md"
```

### Step 1: Load Task File

```
READ the task .poml file
EXTRACT:
  - <metadata><tags> for context focus
  - <knowledge><files> for required reading
  - <constraints> for rules to follow
  - <steps> for execution sequence
  - <metadata><title> for display
  - <metadata><phase> for context
```

### Step 2: Initialize/Update current-task.md

**This step ensures state persistence for context recovery.**

```
LOAD projects/{project-name}/current-task.md

IF file doesn't exist:
  CREATE from template: .claude/templates/current-task.template.md

UPDATE current-task.md:
  - Task ID: {extracted from filename}
  - Task File: {relative path to .poml}
  - Title: {from metadata}
  - Phase: {from metadata}
  - Status: "in-progress"
  - Started: {current timestamp}
  - Clear previous completed_steps (new task)
  - Clear previous files_modified (new task)

COMMIT mentally: This file is now the source of truth for recovery
```

### Step 3: Context Budget Check

```
CHECK current context usage

IF > 70%:
  → UPDATE current-task.md with full state (see "Handoff Protocol" below)
  → REPORT: "Context at {X}%. Handoff saved to current-task.md."
  → Request new session
  → STOP until fresh context available

IF > 85%:
  → EMERGENCY: Immediately update current-task.md
  → STOP work
```

### Step 4: Load Knowledge Files (MANDATORY)

**This step is NOT optional. Do NOT skip to implementation.**

```
FOR each file in <knowledge><files>:
  READ the file completely
  EXTRACT key rules, patterns, constraints

FOR each pattern in <knowledge><patterns>:
  READ the referenced file
  UNDERSTAND the pattern to follow

COMMON KNOWLEDGE FILES BY TAG:
  - pcf tags → READ src/client/pcf/CLAUDE.md
  - pcf tags → READ docs/ai-knowledge/guides/PCF-V9-PACKAGING.md
  - bff-api tags → READ src/server/api/CLAUDE.md (if exists)
  - dataverse tags → READ .claude/skills/dataverse-deploy/SKILL.md
  - deploy tags → READ .claude/skills/dataverse-deploy/SKILL.md

UPDATE current-task.md:
  - Add "Knowledge Files Loaded" section with paths
```

### Step 5: Load ADR Constraints

```
FOR each <constraint source="ADR-XXX">:
  READ docs/adr/ADR-XXX-*.md
  NOTE specific requirements that apply

UPDATE current-task.md:
  - Add "Applicable ADRs" section with ADR numbers and relevance
```

### Step 6: Apply Always-Apply Skills

```
LOAD .claude/skills/adr-aware/SKILL.md
LOAD .claude/skills/spaarke-conventions/SKILL.md
```

### Step 7: Review Relevant CLAUDE.md Files

```
BASED on <context><relevant-files>:
  IDENTIFY module paths (src/client/pcf/*, src/server/api/*, etc.)
  FOR each module:
    IF {module}/CLAUDE.md exists:
      READ for module-specific conventions
```

### Step 8: Execute Task Steps (with Progress Tracking)

```
FOR each <step> in <steps>:

  BEFORE starting step:
    UPDATE current-task.md:
      - Current Step: Step {N} - {description}
      - Next Action: {what this step will do}

  EXECUTE the step

  IF step involves code changes:
    APPLY patterns from <knowledge>
    VERIFY against <constraints>

    UPDATE current-task.md → Files Modified:
      - Add each file touched with purpose

  IF step involves PCF:
    FOLLOW PCF-V9-PACKAGING.md version bumping rules
    UPDATE version in 4 locations

  IF step involves deployment:
    FOLLOW dataverse-deploy skill

  IF decision made:
    UPDATE current-task.md → Decisions Made:
      - {timestamp}: {decision} — Reason: {why}

  AFTER completing step:
    UPDATE current-task.md → Completed Steps:
      - [x] Step {N}: {description} ({timestamp})

    UPDATE Next Step to Step {N+1}
```

### Step 9: Verify Acceptance Criteria

```
FOR each <criterion> in <acceptance-criteria>:
  VERIFY the criterion is met
  IF not met:
    FIX before proceeding
```

### Step 10: Update Task Status (Completion)

```
UPDATE task file <metadata><status> to "completed"
ADD <notes> section with completion summary

UPDATE TASK-INDEX.md with ✅ completed status

UPDATE current-task.md:
  - Status: "completed"
  - All steps marked [x]
  - Clear "Next Action" (task done)
  - Add completion timestamp

DETERMINE next task:
  - Check TASK-INDEX.md for next pending task
  - If found: Update current-task.md with next task info (status: "not-started")
  - If none: Set current-task.md status to "none"
```

---

## Handoff Protocol (Pre-Compaction)

When context usage is high or session ending:

```
UPDATE current-task.md completely:

1. Completed Steps: Mark all finished steps with timestamps

2. Current Step: Document exactly where you are:
   - What was being done
   - What's left to do on this step

3. Files Modified: Complete list with purposes

4. Decisions Made: All implementation choices with rationale

5. Session Notes → Handoff Notes:
   - Key context not captured elsewhere
   - Gotchas discovered
   - Important warnings
   - "Another Claude should know..."

6. Next Action: Clear, specific next step

VERIFY: Another Claude instance could continue from current-task.md alone

REPORT to user:
  "✅ State saved to current-task.md
   Ready for /compact or new session.
   Run 'continue task' or 'where was I?' to resume."
```

---

## Context Recovery Protocol

When resuming work (new session or post-compaction):

```
1. READ projects/{project-name}/current-task.md
2. EXTRACT active task info
3. LOAD the task .poml file
4. LOAD knowledge files from task
5. LOAD ADRs from constraints
6. RESUME from "Current Step"

REPORT:
  "✅ Context recovered for {project-name}

   Task: {task_id} - {title}
   Completed: Steps 1-{N}
   Resuming: Step {N+1} - {description}

   Ready to continue. Proceed?"
```

---

## PCF-Specific Checklist

When task has `pcf`, `react`, or `fluent-ui` tags:

- [ ] Read `src/client/pcf/CLAUDE.md`
- [ ] Read `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md`
- [ ] Version bumped in ControlManifest.Input.xml
- [ ] Version bumped in Solution.xml
- [ ] Version bumped in extracted ControlManifest.xml
- [ ] Version shown in UI footer
- [ ] Build succeeds: `npm run build`
- [ ] If deploying: Use `pac pcf push` or solution import
- [ ] current-task.md updated with files modified

---

## BFF API-Specific Checklist

When task has `bff-api`, `api`, or `minimal-api` tags:

- [ ] Read `src/server/api/CLAUDE.md` (if exists)
- [ ] Follow ADR-001 Minimal API patterns
- [ ] Follow ADR-008 endpoint filter patterns
- [ ] Build succeeds: `dotnet build`
- [ ] Tests pass: `dotnet test`
- [ ] current-task.md updated with files modified

---

## Deployment-Specific Checklist

When task has `deploy` tag:

- [ ] Read `.claude/skills/dataverse-deploy/SKILL.md`
- [ ] Read `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md`
- [ ] Fresh build (delete bin/obj first)
- [ ] Version numbers correct
- [ ] PAC CLI authenticated
- [ ] Deployment verified
- [ ] current-task.md updated with deployment status

---

## Example Execution

**User**: "Execute task 013"

**Agent**:
```
1. CHECK current-task.md for recovery state
2. READ tasks/013-update-aisummarypanel.poml
3. UPDATE current-task.md: task 013, status: in-progress
4. EXTRACT tags: pcf, react, fluent-ui, frontend
5. LOAD KNOWLEDGE:
   - READ src/client/pcf/CLAUDE.md
   - READ docs/ai-knowledge/guides/PCF-V9-PACKAGING.md
   - UPDATE current-task.md with knowledge files loaded
6. LOAD CONSTRAINTS:
   - ADR-006, ADR-021: PCF patterns
   - UPDATE current-task.md with applicable ADRs
7. LOAD ALWAYS-APPLY:
   - adr-aware skill
   - spaarke-conventions skill
8. EXECUTE STEPS 1-8 from task:
   - UPDATE current-task.md after each step
   - Track files modified
   - Log decisions made
9. VERIFY acceptance criteria
10. UPDATE task status to completed
11. UPDATE current-task.md: status completed, set next task
```

---

## Example Recovery

**User**: "Where was I?" (after new session)

**Agent**:
```
1. READ projects/ai-doc-summary/current-task.md
2. FOUND: Task 013, status: in-progress, step 5 of 8 complete
3. LOAD tasks/013-update-aisummarypanel.poml
4. LOAD knowledge files per task
5. REPORT:
   "✅ Recovered context for ai-doc-summary

   Task: 013 - Update AISummaryPanel component
   Completed: Steps 1-5
   Files modified: src/client/pcf/AISummaryPanel/...

   Next: Step 6 - Add dark mode support

   Ready to continue?"
```

---

## Failure Modes to Avoid

| Failure | Cause | Prevention |
|---------|-------|------------|
| Lost progress after compaction | Didn't update current-task.md | ALWAYS update after each step |
| Stale PCF deployment | Didn't bump version | ALWAYS read PCF-V9-PACKAGING.md |
| ADR violation | Didn't read ADRs | ALWAYS read ADRs in constraints |
| Wrong patterns | Didn't read CLAUDE.md | ALWAYS read module CLAUDE.md |
| Missing context | Skipped knowledge step | NEVER skip Step 4 |
| Can't resume | current-task.md incomplete | Update ALL sections during work |

---

## Related Skills

- **task-create**: Creates the task files this skill executes
- **adr-aware**: Proactive ADR loading (always-apply)
- **dataverse-deploy**: Deployment operations
- **code-review**: Post-implementation review
- **project-pipeline**: Initializes current-task.md for projects

## Related Protocols

- **[Context Recovery Protocol](../../../docs/procedures/context-recovery.md)**: Full recovery procedure

---

*This skill ensures Claude Code maintains recoverable state across all context boundaries.*
