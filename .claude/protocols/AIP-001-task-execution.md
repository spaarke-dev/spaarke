# AIP-001: Task Execution Protocol

> **Status**: Active  
> **Created**: December 4, 2025  
> **Applies To**: All AI agents executing tasks in Spaarke projects

---

## Summary

This protocol defines how AI agents execute tasks, manage context, and handle session handoffs. Following this protocol ensures consistent, reliable task completion with proper human oversight.

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

When context exceeds 70%, create a handoff:

### Handoff Location
`projects/{project-name}/notes/handoffs/handoff-{NNN}.md`

### Handoff Template
```markdown
# Handoff Summary - Task {ID}

## Task
- **ID**: {task-id}
- **Title**: {task-title}
- **File**: `tasks/{task-file}.poml`

## Completed
- [x] Subtask 1
- [x] Subtask 2

## Remaining
- [ ] Subtask 3
- [ ] Subtask 4

## Files Modified
- `src/path/file.cs` - Description

## Decisions Made
- Choice A because {reason}

## Resources for Next Session
- `docs/adr/ADR-XXX.md`
- `src/path/pattern.cs`
```

### After Creating Handoff

Tell the user:
```
Context at {X}%. Created handoff at:
projects/{project-name}/notes/handoffs/handoff-{NNN}.md

Please start new session with:
"Continue from handoff: projects/{project-name}/notes/handoffs/handoff-{NNN}.md"
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
User: "Execute task: projects/my-project/tasks/001-setup.poml"

AI:
1. Check context: 23% ✅
2. Read TASK-INDEX.md - no prior work
3. Read task file, gather resources
4. Plan: 3 subtasks
5. Implement subtask 1, context now 35%
6. Implement subtask 2, context now 48%
7. Implement subtask 3, context now 62%
8. Run tests ✅
9. Update TASK-INDEX.md
10. Report completion
```

### Example: Handoff Required
```
User: "Continue implementing the API endpoints"

AI:
1. Check context: 73% 🛑
2. Create handoff summary
3. Tell user: "Context at 73%. Created handoff..."
4. STOP - do not proceed
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

## Related Protocols

- [AIP-002: POML Format](AIP-002-poml-format.md) - Task file structure
- [AIP-003: Human Escalation](AIP-003-human-escalation.md) - When to request input

---

*Part of [AI Protocols](INDEX.md) | Spaarke AI Knowledge Base*
