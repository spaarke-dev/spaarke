# Current Task State

> **Auto-updated by task-execute skill**
> **Last Updated**: {YYYY-MM-DD HH:MM}
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Active Task

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

<!-- Updated by task-execute after each step completion -->
<!-- Format: - [x] Step N: {description} ({YYYY-MM-DD HH:MM}) -->

*No steps completed yet*

### Current Step

**Step {N}**: {Step description from POML}

**What this step involves**:
- {Sub-action 1}
- {Sub-action 2}

### Files Modified

<!-- Track all files created or modified during this task -->
<!-- Format: - `path/to/file` - {Created|Modified} - {brief purpose} -->

*No files modified yet*

### Decisions Made

<!-- Log implementation decisions for context recovery -->
<!-- Format: - {YYYY-MM-DD}: {Decision} — Reason: {why} -->

*No decisions recorded yet*

---

## Next Action

**Next Step**: {Step N} - {Step description}

**Pre-conditions**:
- {What must be true before starting}

**Key Context**:
- Refer to `{file}` for {reason}
- ADR-{XXX} applies: {constraint summary}

**Expected Output**:
- {What completing this step produces}

---

## Blockers

<!-- List anything preventing progress -->

**Status**: {None / Blocked}

{If blocked: Description of blocker and what's needed to resolve}

---

## Session Notes

<!-- Free-form notes for current session context -->
<!-- These persist across compaction for context recovery -->

### Current Session
- Started: {YYYY-MM-DD HH:MM}
- Focus: {What we're working on}

### Key Learnings
<!-- Gotchas, warnings, or important discoveries -->

*None yet*

### Handoff Notes
<!-- Used when context budget is high or session ending -->
<!-- Another Claude instance should be able to continue from these notes -->

*No handoff notes*

---

## Quick Reference

### Project Context
- **Project**: {project-name}
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
<!-- From task constraints -->
- ADR-{XXX}: {title} - {one-line relevance}

### Knowledge Files Loaded
<!-- From task knowledge section -->
- `{path}` - {purpose}

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. Read this file (`current-task.md`)
2. Load the task file listed above
3. Load knowledge files from the task's `<knowledge>` section
4. Resume from the "Current Step" section

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
