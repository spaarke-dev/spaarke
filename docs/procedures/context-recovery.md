# Context Recovery Protocol

> **Purpose**: Recover full working context after Claude Code compaction, session restart, or context reset
> **Last Updated**: January 4, 2026
> **Protocol ID**: CRP-001

---

## Overview

Claude Code's context window is finite. When context usage exceeds thresholds or a new session starts, critical working state can be lost. This protocol ensures Claude Code can recover full project and task context from files alone.

**Design Principle**: Everything Claude needs to continue work must be recoverable from persistent files. No critical state exists only in conversation history.

### Key Skills for Context Management

| Skill | Purpose | When to Use |
|-------|---------|-------------|
| **context-handoff** | Save state before compaction | "Save my progress", `/context-handoff` |
| **project-continue** | Restore state after compaction | "Where was I?", `/project-continue` |

These skills work together: `context-handoff` creates checkpoints, `project-continue` recovers from them.

---

## How to Use This Protocol (Developer Guide)

### Starting a New Session

When you open Claude Code in a new session and want to continue previous work:

```
# Option 1: Quick resume (recommended) - triggers project-continue skill
You: "Where was I?"

# Option 2: Resume specific project
You: "Continue work on ai-doc-summary"

# Option 3: Resume specific task
You: "Continue task 013"

# Option 4: Check all project status first
You: "/project-status"

# Option 5: Explicit skill invocation
You: "/project-continue ai-document-intelligence-r3"
```

Claude Code will automatically invoke the **project-continue** skill, which:
1. Syncs your branch with master (pull latest changes)
2. Checks PR status if one exists
3. Loads all project context (CLAUDE.md, plan.md, spec.md, README.md)
4. Reads the **Quick Recovery** section from `current-task.md`
5. Reports what was completed and what's next
6. Asks if you're ready to continue

### During Work (Proactive Updates)

Claude Code automatically updates `current-task.md` during task execution:
- After completing each step
- When modifying files
- When making implementation decisions
- Before context gets too high (>70%)

**You don't need to do anything** — the `task-execute` skill handles this.

### Before Ending a Session

If you need to stop mid-task, invoke the **context-handoff** skill:

```
# Option 1: Natural language (recommended)
You: "Save my progress"

# Option 2: Explicit skill invocation
You: "/context-handoff"

# Option 3: Before manual compaction
You: "I need to compact, save my progress first"
```

Claude Code will invoke the **context-handoff** skill, which:
1. Captures critical state (task ID, current step, files modified, decisions)
2. Updates `current-task.md` with a **Quick Recovery** section at the top
3. Verifies the checkpoint is complete
4. Reports: "✅ State saved. Ready for /compact or session end."

The Quick Recovery section is designed to be readable in < 30 seconds for fast context restoration.

### After Using /compact

The `/compact` command compresses context but loses conversation history:

```
# After compacting
You: "Where was I?"
```

Claude Code recovers from `current-task.md` and continues.

---

## Common Scenarios

### Scenario 1: Resuming a Project After Days/Weeks

You started a project last week and want to continue:

```
You: "I want to continue working on the ai-doc-summary project"
```

Claude Code will:
1. Read `projects/ai-doc-summary/current-task.md`
2. Load project context (CLAUDE.md, README.md, plan.md)
3. Load the active task file and its knowledge requirements
4. Report: "Task 013 was in progress. Steps 1-5 complete. Next: Step 6 - Add dark mode support. Ready to continue?"

### Scenario 2: Starting Fresh Session After Compaction

You ran `/compact` to free up context:

```
You: "Where was I?"
```

Claude Code reads `current-task.md` and resumes exactly where you left off.

### Scenario 3: Task Was Interrupted Mid-Step

You closed Claude Code in the middle of implementing Step 4:

```
You: "Continue task 013"
```

Claude Code will:
1. Load task 013 context
2. See Step 4 was marked "in-progress" (not completed)
3. Read the "Current Step" details from `current-task.md`
4. Resume Step 4 from where you left off

### Scenario 4: Switching Between Projects

You have two projects and want to switch:

```
You: "Switch to the mda-darkmode-theme project"
```

Claude Code will:
1. Save current project state to its `current-task.md`
2. Load the new project's `current-task.md`
3. Resume that project's active task

### Scenario 5: Not Sure What's Active

You forgot what you were working on:

```
You: "/project-status"
```

Shows all projects with their current state, then:

```
You: "Continue the in-progress one"
```

---

## Context Recovery Triggers

| Trigger | What to Say | What Claude Does |
|---------|-------------|------------------|
| New session start | "Where was I?" | Full recovery (Step 1-5) |
| After `/compact` | "Continue task" | Verification recovery (Step 3-5) |
| Context usage > 70% | (automatic) | Creates handoff, requests new session |
| Context usage > 85% | (automatic) | Emergency handoff |
| Switching projects | "Work on {project}" | Full recovery for specified project |

---

## Recovery Protocol (What Claude Does)

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

## How current-task.md Works Across Tasks

**Key concept**: `current-task.md` tracks only the **active task**, not task history.

### Task Lifecycle in current-task.md

```
Task 001 starts
  → current-task.md: Task ID = 001, Status = in-progress
  → Steps completed, files modified tracked

Task 001 completes
  → current-task.md RESETS (clears steps, files, decisions)
  → current-task.md: Task ID = 002, Status = not-started

Task 002 starts
  → current-task.md: Status = in-progress
  → Fresh tracking for this task
```

### Where History Is Preserved

| What | Where |
|------|-------|
| Which tasks are done | `TASK-INDEX.md` (✅/🔲 status) |
| Task completion notes | Individual `.poml` files (`<notes>` section) |
| Code changes per task | Git commits |
| Important cross-task learnings | `current-task.md` → Session Notes → Key Learnings |

### Why Reset Instead of Accumulate?

- **Faster recovery**: Only load current task context, not entire history
- **Focused context**: Recovery needs "where am I now", not "where have I been"
- **Prevents bloat**: 50+ task project would have massive file otherwise
- **History exists elsewhere**: TASK-INDEX.md and git provide full history

---

## current-task.md Format

**Location**: `projects/{project-name}/current-task.md`

**Template**: `.claude/templates/current-task.template.md`

```markdown
# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: {YYYY-MM-DD HH:MM}
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

<!-- This section is for FAST context restoration after compaction -->
<!-- Must be readable in < 30 seconds -->

| Field | Value |
|-------|-------|
| **Task** | {NNN} - {Title} |
| **Step** | {N} of {Total}: {Step description} |
| **Status** | {in-progress / blocked / not-started / none} |
| **Next Action** | {EXPLICIT next action - what command to run or file to edit} |

### Files Modified This Session
<!-- Only files touched in CURRENT session, not all time -->
- `{path}` - {Created/Modified} - {brief purpose}

### Critical Context
<!-- 1-3 sentences of essential context for continuation -->
{What must be understood to continue effectively}

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | {NNN or "none"} |
| **Task File** | `tasks/{NNN}-{slug}.poml` |
| **Title** | {Task title from POML metadata} |
| **Phase** | {Phase number}: {Phase name} |
| **Status** | {not-started / in-progress / blocked / completed / none} |
| **Started** | {YYYY-MM-DD HH:MM or "—"} |

---

## Progress

### Completed Steps
<!-- Updated after each step completion -->
- [x] Step 1: {description} ({timestamp})
- [x] Step 2: {description} ({timestamp})
- [ ] Step 3: {description} ← **Next**

### Files Modified (All Task)
<!-- Track all files touched during this task -->
- `src/path/to/file.ts` - Created/Modified (purpose)
- `src/another/file.cs` - Modified (purpose)

### Decisions Made
<!-- Log implementation decisions for context -->
- {timestamp}: Chose approach X over Y because {reason}

---

## Next Action

**Next Step**: Step 3 - {step description}

**Pre-conditions**:
- {What must be true before starting}

**Key Context**:
- Refer to {file} for {reason}
- ADR-XXX applies to this step

**Blockers**: {none | description of blocker}

---

## Handoff Notes

<!-- Used when context budget is high or session ending -->
{Free-form notes for context handoff}
```

### Quick Recovery Section Purpose

The **Quick Recovery** section at the top enables fast context restoration:

| Requirement | Why |
|-------------|-----|
| Readable in < 30 seconds | Post-compaction recovery must be fast |
| Task + Step in one glance | Know exactly where you are |
| Next Action is EXPLICIT | "Run `dotnet test`" not "Continue working" |
| Session-scoped files | Only current session, not all-time history |
| Critical Context | 1-3 sentences, not paragraphs |

---

## Handoff Creation (Pre-Compaction)

When context usage approaches limits, use the **context-handoff** skill to create a checkpoint.

### Trigger Conditions

| Condition | Trigger Type | Action |
|-----------|--------------|--------|
| Context usage > 70% | Proactive (Claude should self-invoke) | Run context-handoff |
| User requests `/compact` | Manual | User says "save my progress" first |
| Session ending mid-task | Manual | User says "save my progress" |
| After completing 3-5 task steps | Proactive | Claude checkpoints silently |
| After modifying 5+ files | Proactive | Claude runs full context-handoff |

### Using the context-handoff Skill

**Manual invocation:**
```
You: "Save my progress"
# OR
You: "/context-handoff"
```

**The skill performs:**
```
1. IDENTIFY active project
   - Check current worktree or branch
   - Locate projects/{name}/current-task.md

2. CAPTURE critical state
   - Task ID and current step
   - Files modified this session
   - Key decisions made
   - Explicit next action

3. UPDATE current-task.md
   - Populate Quick Recovery section at top
   - Update timestamps
   - Session-scoped file list

4. VERIFY and REPORT
   - Confirm current-task.md is complete
   - Report: "✅ State saved. Ready for /compact."
```

### Proactive Checkpointing (Claude Self-Invokes)

Claude should proactively checkpoint without user prompting:

| After This | Claude Does |
|------------|-------------|
| Completing a task step | Updates current-task.md "Completed Steps" |
| Modifying 5+ files | Runs full context-handoff |
| 30+ minutes of work | Runs full context-handoff |
| Before a large operation | "Let me checkpoint first..." + context-handoff |
| Context feels "heavy" | Check /context, consider checkpoint |

---

## Quick Recovery Commands (Cheat Sheet)

**Most Common:**
| What You Say | What Happens |
|--------------|--------------|
| "Where was I?" | Shows current task, completed steps, next action |
| "Continue" | Resumes active task from where you left off |
| "Continue task 013" | Resumes specific task |

**Project-Level:**
| What You Say | What Happens |
|--------------|--------------|
| "Continue work on {project}" | Full project context load + resume |
| "Switch to {project}" | Save current state, load new project |
| "/project-status" | Overview of all projects and their state |
| "/project-status {project}" | Detailed status of specific project |

**State Inspection:**
| What You Say | What Happens |
|--------------|--------------|
| "Show current state" | Displays current-task.md contents |
| "What files have I modified?" | Lists files touched in current task |
| "What decisions did I make?" | Shows logged implementation decisions |

**Session Management:**
| What You Say | What Happens |
|--------------|--------------|
| "Save my progress" | Updates current-task.md with full state |
| "Create handoff" | Detailed handoff notes for session end |

---

## Integration with Skills

### context-handoff Skill (State Preservation)

The `context-handoff` skill MUST:
1. Identify the active project from worktree or branch
2. Capture critical state (task, step, files, decisions)
3. Update `current-task.md` with Quick Recovery section
4. Verify checkpoint is complete before reporting

**Location**: `.claude/skills/context-handoff/SKILL.md`

### project-continue Skill (State Recovery)

The `project-continue` skill MUST:
1. Sync branch with master before loading context
2. Check PR status if applicable
3. Load ALL project files (CLAUDE.md, plan.md, spec.md, README.md)
4. Read Quick Recovery section from `current-task.md`
5. Load ADRs via adr-aware skill
6. Determine resume point and hand off to task-execute

**Location**: `.claude/skills/project-continue/SKILL.md`

### task-execute Skill (Task Execution)

The `task-execute` skill MUST:
1. Initialize/update `current-task.md` at task start
2. Update `completed_steps` after each step
3. Update `files_modified` when touching files
4. Log decisions in `decisions_made`
5. Update on task completion (status → completed, clear next_step)
6. **Proactively checkpoint** after every 3-5 steps

**Location**: `.claude/skills/task-execute/SKILL.md`

### project-pipeline Skill (Project Initialization)

The `project-pipeline` skill MUST:
1. Create `current-task.md` during Step 2 (artifact generation)
2. Initialize with `status: none` until task execution starts
3. Reference this protocol in generated `CLAUDE.md`

**Location**: `.claude/skills/project-pipeline/SKILL.md`

### repo-cleanup Skill (Project Finalization)

The `repo-cleanup` skill should:
1. Check `current-task.md` for `status: none` or `status: completed`
2. Archive or reset `current-task.md` for completed projects
3. Warn if `current-task.md` shows work in progress

**Location**: `.claude/skills/repo-cleanup/SKILL.md`

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

### Skills
- [context-handoff Skill](../../.claude/skills/context-handoff/SKILL.md) - State preservation before compaction
- [project-continue Skill](../../.claude/skills/project-continue/SKILL.md) - State recovery after compaction
- [task-execute Skill](../../.claude/skills/task-execute/SKILL.md) - Task execution with context persistence
- [project-pipeline Skill](../../.claude/skills/project-pipeline/SKILL.md) - Project initialization

### Templates
- [current-task.template.md](../../.claude/templates/current-task.template.md) - Template with Quick Recovery section

### Protocols
- [AIP-001: Task Execution Protocol](../../.claude/protocols/AIP-001-task-execution.md) - Task execution and handoff rules
- [Root CLAUDE.md](../../CLAUDE.md) - Context management rules

---

*This protocol ensures Claude Code can always recover full working context from persistent files, enabling reliable continuation of work across any context boundary.*
