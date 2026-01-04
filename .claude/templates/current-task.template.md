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

<!-- Updated by task-execute after each step completion -->
<!-- Format: - [x] Step N: {description} ({YYYY-MM-DD HH:MM}) -->

*No steps completed yet*

### Current Step

**Step {N}**: {Step description from POML}

**What this step involves**:
- {Sub-action 1}
- {Sub-action 2}

### Files Modified (All Task)

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

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Load knowledge files**: From task's `<knowledge>` section
5. **Resume**: From the "Next Action" section

**Commands**:
- `/project-continue` - Full project context reload + master sync
- `/context-handoff` - Save current state before compaction
- "where was I?" - Quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
