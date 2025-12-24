# Context Recovery Protocol

> **Purpose**: Recover full working context after Claude Code compaction, session restart, or context reset
> **Last Updated**: December 24, 2025
> **Protocol ID**: CRP-001

---

## Overview

Claude Code's context window is finite. When context usage exceeds thresholds or a new session starts, critical working state can be lost. This protocol ensures Claude Code can recover full project and task context from files alone.

**Design Principle**: Everything Claude needs to continue work must be recoverable from persistent files. No critical state exists only in conversation history.

---

## Context Recovery Triggers

| Trigger | Action Required |
|---------|-----------------|
| New session start | Full recovery (Step 1-5) |
| After `/compact` command | Verification recovery (Step 3-5) |
| Context usage > 70% | Create handoff, then recovery in new session |
| Context usage > 85% | Emergency handoff, immediate new session |
| "Where was I?" question | Quick recovery (Step 3-4) |

---

## Recovery Protocol

### Step 1: Identify Active Project

```
READ: projects/*/current-task.md
  - Find any file where status != "none"
  - This is the active project

IF no active project found:
  → Check TASK-INDEX.md files for 🚧 in-progress tasks
  → Check recent git commits for project context
  → ASK user: "Which project should I continue working on?"
```

### Step 2: Load Project Context

```
FOR active project at projects/{project-name}/:

LOAD (in order):
  1. projects/{project-name}/CLAUDE.md
     → Project-specific constraints, decisions, notes

  2. projects/{project-name}/current-task.md
     → Active task, completed steps, next actions

  3. projects/{project-name}/README.md
     → Project scope, graduation criteria

  4. projects/{project-name}/plan.md
     → Phase structure, current phase context
```

### Step 3: Load Task Context

```
FROM current-task.md, extract:
  - task_id: Current task number
  - task_file: Path to .poml file

LOAD task file:
  projects/{project-name}/tasks/{task_id}-*.poml

EXTRACT from task:
  - <metadata><tags> for knowledge file mapping
  - <knowledge><files> for required reading
  - <constraints> for ADR requirements
  - <steps> for execution sequence
```

### Step 4: Load Knowledge Files

```
FOR each file in <knowledge><files>:
  READ the knowledge file

FOR each constraint source="ADR-XXX":
  READ docs/adr/ADR-XXX-*.md

APPLY tag-based knowledge loading:
  - pcf tags → src/client/pcf/CLAUDE.md
  - bff-api tags → src/server/api/CLAUDE.md
  - deploy tags → .claude/skills/dataverse-deploy/SKILL.md
```

### Step 5: Verify State and Continue

```
VERIFY current-task.md state:
  - completed_steps: List of completed step numbers
  - files_modified: List of files touched
  - next_step: Where to resume

VERIFY file state:
  FOR each file in files_modified:
    CHECK file exists and matches expected state

REPORT to user:
  "✅ Context recovered for {project-name}

   Current task: {task_id} - {task_title}
   Completed steps: {completed_steps}
   Next step: {next_step}

   Ready to continue. Proceed?"
```

---

## Required Context Files

### Root Level (Always Available)

| File | Purpose | Recovery Role |
|------|---------|---------------|
| `CLAUDE.md` | Repository-wide context | Load first for global constraints |
| `.claude/skills/INDEX.md` | Skill registry | Reference for skill discovery |

### Project Level (Per Project)

| File | Purpose | Recovery Role |
|------|---------|---------------|
| `projects/{name}/CLAUDE.md` | Project-specific AI context | Project constraints, decisions |
| `projects/{name}/current-task.md` | **Active state tracker** | **Primary recovery source** |
| `projects/{name}/README.md` | Project overview | Scope, graduation criteria |
| `projects/{name}/plan.md` | Implementation plan | Phase context |
| `projects/{name}/tasks/TASK-INDEX.md` | Task registry | Task status overview |

### Task Level (Per Task)

| File | Purpose | Recovery Role |
|------|---------|---------------|
| `tasks/{NNN}-*.poml` | Task definition | Steps, constraints, acceptance criteria |
| Knowledge files (per task) | Domain knowledge | Required reading for implementation |

---

## current-task.md Format

**Location**: `projects/{project-name}/current-task.md`

```markdown
# Current Task State

> **Auto-updated by task-execute skill**
> **Last Updated**: {ISO timestamp}

## Active Task

- **Task ID**: {NNN}
- **Task File**: `tasks/{NNN}-{slug}.poml`
- **Title**: {Task title from POML}
- **Phase**: {Phase number and name}
- **Status**: {not-started | in-progress | blocked | completed | none}

## Progress

### Completed Steps
<!-- Updated after each step completion -->
- [x] Step 1: {description} ({timestamp})
- [x] Step 2: {description} ({timestamp})
- [ ] Step 3: {description} ← **Next**

### Files Modified
<!-- Track all files touched during this task -->
- `src/path/to/file.ts` - Created/Modified (purpose)
- `src/another/file.cs` - Modified (purpose)

### Decisions Made
<!-- Log implementation decisions for context -->
- {timestamp}: Chose approach X over Y because {reason}

## Next Action

**Next Step**: Step 3 - {step description}

**Context Needed**:
- Refer to {file} for {reason}
- ADR-XXX applies to this step

**Blockers**: {none | description of blocker}

## Handoff Notes

<!-- Used when context budget is high or session ending -->
{Free-form notes for context handoff}
```

---

## Handoff Creation (Pre-Compaction)

When context usage approaches limits, create a handoff summary before compaction:

### Trigger Conditions
- Context usage > 70%
- User requests `/compact`
- Session ending with work in progress

### Handoff Procedure

```
1. UPDATE current-task.md with latest state
   - All completed steps
   - All files modified
   - Clear next action

2. ADD handoff notes section
   - Key decisions made this session
   - Gotchas or warnings discovered
   - Important context not in other files

3. VERIFY current-task.md is complete
   - Another Claude instance could continue from this file alone

4. COMMIT current-task.md (optional but recommended)
   git add projects/{name}/current-task.md
   git commit -m "chore: update task state for handoff"

5. REPORT to user:
   "✅ Handoff created at projects/{name}/current-task.md

    Ready for /compact or new session.
    Context can be fully recovered from files."
```

---

## Quick Recovery Commands

| Command | Action |
|---------|--------|
| "Where was I?" | Run Step 3-5 of recovery protocol |
| "Continue task" | Full recovery + resume execution |
| "Show current state" | Display current-task.md contents |
| "Recover context for {project}" | Run full recovery for specified project |

---

## Integration with Skills

### task-execute Skill

The `task-execute` skill MUST:
1. Initialize/update `current-task.md` at task start
2. Update `completed_steps` after each step
3. Update `files_modified` when touching files
4. Log decisions in `decisions_made`
5. Update on task completion (status → completed, clear next_step)

### project-pipeline Skill

The `project-pipeline` skill MUST:
1. Create `current-task.md` during Step 2 (artifact generation)
2. Initialize with `status: none` until task execution starts
3. Reference this protocol in generated `CLAUDE.md`

### repo-cleanup Skill

The `repo-cleanup` skill should:
1. Check `current-task.md` for `status: none` or `status: completed`
2. Archive or reset `current-task.md` for completed projects
3. Warn if `current-task.md` shows work in progress

---

## Verification Checklist

After recovery, verify:

- [ ] Active project identified
- [ ] Project CLAUDE.md loaded
- [ ] current-task.md loaded and parsed
- [ ] Task .poml file loaded
- [ ] Knowledge files loaded per task tags
- [ ] Relevant ADRs loaded per constraints
- [ ] Files modified list verified against filesystem
- [ ] Next step clearly identified
- [ ] Ready to continue work

---

## Failure Modes and Recovery

| Failure | Recovery |
|---------|----------|
| current-task.md missing | Check TASK-INDEX.md for in-progress tasks |
| current-task.md stale | Check git log for recent file changes |
| Task file missing | Check tasks/ directory listing |
| Knowledge file missing | Warn user, proceed with available context |
| Conflicting state | Ask user to clarify current work |

---

## Related Documents

- [task-execute Skill](.claude/skills/task-execute/SKILL.md) - Task execution with context persistence
- [project-pipeline Skill](.claude/skills/project-pipeline/SKILL.md) - Project initialization
- [Root CLAUDE.md](../../CLAUDE.md) - Context management rules

---

*This protocol ensures Claude Code can always recover full working context from persistent files, enabling reliable continuation of work across any context boundary.*
