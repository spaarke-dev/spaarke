# AIP-001: Task Execution Protocol

> **Status**: Active
> **Created**: December 4, 2025
> **Updated**: January 4, 2026
> **Applies To**: All AI agents executing tasks in Spaarke projects

---

## Summary

This protocol defines how AI agents execute tasks, manage context, and handle session handoffs. Following this protocol ensures consistent, reliable task completion with proper human oversight.

### Key Skills

| Skill | Role in Task Execution |
|-------|------------------------|
| **task-execute** | Execute POML tasks with context loading |
| **context-handoff** | Save state before compaction (invoked proactively or manually) |
| **project-continue** | Restore state after compaction or new session |

---

## Context Management Rules

### Rule 1: Monitor Context Usage

Check context usage at:
- Start of every task
- After each subtask completion
- Before loading large files

### Rule 2: Context Thresholds

| Usage | Level | Action |
|-------|-------|--------|
| < 50% | ✅ Normal | Proceed normally |
| 50-70% | ⚠️ Warning | Complete current subtask, then assess |
| > 70% | 🛑 Critical | STOP - Create handoff, request new session |
| > 85% | 🚨 Emergency | Immediately create handoff |

### Rule 3: Context Commands

| Command | Purpose |
|---------|---------|
| `/context` | Check current usage |
| `/clear` | Wipe context |
| `/compact` | Compress to reclaim space |

---

## Task Execution Steps

### Step 0: Context Check
```
IF context > 70%:
    Create handoff summary
    Request new session
ELSE:
    Proceed
```

### Step 1: Review Progress
- Read project `README.md` and `TASK-INDEX.md`
- Verify dependencies are complete
- Check for previous partial work in `notes/handoffs/`

### Step 2: Gather Resources
- Read files in `<knowledge>` section of task
- Load applicable ADRs from `<constraints>`
- Find existing patterns to follow

### Step 3: Plan Implementation
- Break task into subtasks
- Identify code patterns to follow
- List files to create/modify

### Step 4: Implement
- Execute subtasks in order
- Write tests alongside code
- Context check after each subtask

### Step 5: Verify
- Run tests: `dotnet test` / `npm test`
- Verify build succeeds
- Check acceptance criteria

### Step 6: Document
- Update `TASK-INDEX.md` status: 🔲 → ✅
- Document deviations in `notes/`
- Report completion to user

---

## Handoff Protocol

When context exceeds 70%, use the **context-handoff** skill to save state.

### Primary State File
`projects/{project-name}/current-task.md`

**Important**: We use `current-task.md` as the single source of truth for task state, not separate handoff files. This ensures consistent recovery.

### Invoking context-handoff

**Proactive (Claude self-invokes):**
- After completing 3-5 task steps
- After modifying 5+ files
- When context > 70%
- Before large operations

**Manual (User requests):**
```
User: "Save my progress"
# OR
User: "/context-handoff"
```

### What context-handoff Does

```
1. IDENTIFY active project
   - Check worktree or branch name
   - Locate projects/{name}/current-task.md

2. CAPTURE critical state
   - Task ID and current step (N of M)
   - Files modified this session
   - Key decisions made
   - EXPLICIT next action (command or file to edit)

3. UPDATE current-task.md
   - Populate Quick Recovery section at top
   - Update "Last Updated" timestamp
   - Session-scoped file list

4. VERIFY and REPORT
   "✅ State saved to current-task.md
    Task: {id} - {title}
    Step: {N} of {M}
    Next: {explicit action}

    Ready for /compact or session end."
```

### Quick Recovery Section Format

The Quick Recovery section at the top of `current-task.md` enables fast restoration:

```markdown
## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 013 - Add dark mode support |
| **Step** | 4 of 7: Create theme toggle component |
| **Status** | in-progress |
| **Next Action** | Run `npm run build:prod` then deploy with `/dataverse-deploy` |

### Files Modified This Session
- `src/client/pcf/ThemeToggle/index.ts` - Created component
- `src/client/pcf/ThemeToggle/ThemeToggle.tsx` - Main implementation

### Critical Context
Theme context provider implemented. Dark mode CSS variables defined.
Need to test toggle behavior before deploying.
```

### After Compaction or New Session

User says "where was I?" or "continue" which triggers **project-continue** skill:

```
1. Sync with master
2. Load project context (CLAUDE.md, plan.md, README.md)
3. Read Quick Recovery section from current-task.md
4. Report state and resume
```

---

## Session Strategy

| Situation | Recommendation |
|-----------|----------------|
| Starting new phase | Fresh session |
| Context > 70% | Handoff → fresh session |
| Complex task | Break into subtasks, checkpoint between |
| Between phases | Fresh session recommended |

---

## Examples

### Example: Normal Task Execution
```
User: "Work on task 001"

AI (via task-execute skill):
1. Check context: 23% ✅
2. Load task file, knowledge, ADRs
3. Read current-task.md - no prior work
4. Plan: 3 subtasks
5. Implement subtask 1, context now 35%
   → Update current-task.md "Completed Steps"
6. Implement subtask 2, context now 48%
   → Update current-task.md "Completed Steps"
7. Implement subtask 3, context now 62%
   → Run context-handoff (after 3 steps)
   → Update current-task.md with Quick Recovery section
8. Run tests ✅
9. Update TASK-INDEX.md status
10. Report completion
```

### Example: Proactive Checkpoint
```
[AI working on large task, after step 5]

AI:
1. Note: 5 steps complete, context at 55%
2. Self-invoke context-handoff
3. Update current-task.md Quick Recovery section
4. "✅ Checkpoint saved. Continuing with step 6..."
5. Continue working
```

### Example: Context Threshold Reached
```
User: "Continue implementing the API endpoints"

AI:
1. Check context: 73% 🛑
2. Invoke context-handoff skill
3. Update current-task.md with full state
4. Tell user:
   "Context at 73%. State saved to current-task.md.
    Task: 013, Step: 4 of 7
    Next: Add rate limiting to endpoints

    Ready for /compact. After compaction, say 'where was I?'"
5. STOP - do not proceed
```

### Example: Recovery After Compaction
```
[After user runs /compact]

User: "Where was I?"

AI (via project-continue skill):
1. Sync with master: Already up to date
2. Load project context: CLAUDE.md, plan.md, README.md
3. Read current-task.md Quick Recovery section
4. Report:
   "✅ Context recovered

    Task: 013 - Add API rate limiting
    Step: 4 of 7: Implement rate limit middleware
    Next: Add rate limiting to endpoints

    Ready to continue?"
```

---

## Rationale

### Why context thresholds?
Claude's context window is finite (~200K tokens). As context fills:
- Response quality degrades
- Important context may be truncated
- Risk of losing progress increases

70% threshold provides safety margin for completing current work and creating handoff.

### Why handoff summaries?
- Enable seamless continuation in fresh session
- Preserve decisions and progress
- Reduce duplicate work

---

## Related Documents

### Protocols
- [AIP-002: POML Format](AIP-002-poml-format.md) - Task file structure
- [AIP-003: Human Escalation](AIP-003-human-escalation.md) - When to request input

### Skills
- [context-handoff](../skills/context-handoff/SKILL.md) - State preservation before compaction
- [project-continue](../skills/project-continue/SKILL.md) - State recovery after compaction
- [task-execute](../skills/task-execute/SKILL.md) - Task execution with context loading

### Procedures
- [Context Recovery Protocol](../../docs/procedures/context-recovery.md) - Full context recovery procedure

### Templates
- [current-task.template.md](../templates/current-task.template.md) - Task state file template

---

*Part of [AI Protocols](INDEX.md) | Spaarke AI Knowledge Base*
