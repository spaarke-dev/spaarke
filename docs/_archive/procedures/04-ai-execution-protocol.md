# AI Task Execution Protocol

> **Audience**: AI Agents (Claude Code)  
> **Load this document**: At the start of every task execution session  
> **Part of**: [Spaarke Software Development Procedures](INDEX.md)

---

## Purpose

This document defines the execution protocol AI agents must follow when executing tasks. It specifies context management, human-in-the-loop triggers, and session handoff procedures.

Before executing **Task 001** in a project, ensure the project is in a safe, “ready-to-execute” state:
- You are working on a non-`master` branch (see `09-repo-and-github-process.md`)
- `tasks/TASK-INDEX.md` matches the task files present in `tasks/`
- If `projects/{project-name}/scripts/audit-tasks.ps1` exists, run it and require a passing result

---

## Task Execution Protocol

For each task, follow these steps in order:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     TASK EXECUTION PROTOCOL                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  STEP 0: CONTEXT CHECK                                                  │
│  • Check context usage (aim for < 70%)                                  │
│  • If > 70%: Create handoff summary → Request new session               │
│  • If < 70%: Proceed                                                    │
│                              │                                          │
│                              ▼                                          │
│  STEP 1: REVIEW PROGRESS                                                │
│  • Read project README.md and TASK-INDEX.md                             │
│  • Verify dependencies are complete                                     │
│  • Check for previous partial work                                      │
│                              │                                          │
│                              ▼                                          │
│  STEP 2: GATHER RESOURCES                                               │
│  • Read all files in <inputs> and <knowledge>                           │
│  • Load applicable ADRs                                                 │
│  • Find existing patterns to follow                                     │
│                              │                                          │
│                              ▼                                          │
│  STEP 3: PLAN IMPLEMENTATION                                            │
│  • Break into subtasks                                                  │
│  • Identify code patterns to follow                                     │
│  • List files to create/modify                                          │
│                              │                                          │
│                              ▼                                          │
│  STEP 4: IMPLEMENT                                                      │
│  • Execute subtasks in order                                            │
│  • Write tests alongside code                                           │
│  • Context check after each subtask                                     │
│                              │                                          │
│                              ▼                                          │
│  STEP 5: VERIFY                                                         │
│  • Run tests (dotnet test / npm test)                                   │
│  • Verify build succeeds                                                │
│  • Check acceptance criteria                                            │
│                              │                                          │
│                              ▼                                          │
│  STEP 6: DOCUMENT                                                       │
│  • Update TASK-INDEX.md status (🔲 → ✅)                                │
│  • Document any deviations in notes/                                    │
│  • Generate completion report                                           │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Context Thresholds

Claude Code has ~200K token context window. Monitor and act on these thresholds:

| Usage | Level | Action |
|-------|-------|--------|
| < 50% | Normal | ✅ Proceed normally |
| 50-70% | Warning | ⚠️ Monitor, complete current subtask |
| > 70% | Critical | 🛑 STOP - Create handoff, request new session |
| > 85% | Emergency | 🚨 Immediately create handoff |

### Context Commands

| Command | Purpose |
|---------|---------|
| `/context` | Display current context usage |
| `/clear` | Wipe conversation context |
| `/compact` | Compress conversation |

---

## Handoff Protocol

When context exceeds 70%, create a handoff summary:

### Handoff Location
`projects/{project-name}/notes/handoffs/handoff-{NNN}.md`

### Handoff Template

```markdown
# Handoff Summary - Task {ID}

## Task
- **ID**: {task-id}
- **Title**: {task-title}
- **File**: `tasks/{task-file}.poml`

## Progress
### Completed
- [x] Subtask 1 description
- [x] Subtask 2 description

### Remaining
- [ ] Subtask 3 description
- [ ] Subtask 4 description

## Files Modified
- `src/path/to/file1.cs` - Added method X
- `src/path/to/file2.ts` - Created new component

## Decisions Made
- Chose approach A over B because {reason}
- Interpreted requirement X as {interpretation}

## Blockers / Questions
- None / List any

## Context for Next Session
{Key information the next session needs to continue}

## Resources to Load
- `docs/reference/adr/ADR-XXX.md`
- `src/path/to/pattern-file.cs`

```

### After Creating Handoff

Tell the user:
```
Context at {X}%. Created handoff summary at:
projects/{project-name}/notes/handoffs/handoff-{NNN}.md

Please start a new session with:
"Continue task from handoff: projects/{project-name}/notes/handoffs/handoff-{NNN}.md"
```

---

## Human-in-the-Loop Triggers

### MUST Request Human Input When:

| Situation | Action |
|-----------|--------|
| **Ambiguous requirement** | Ask for clarification before proceeding |
| **Multiple valid approaches** | Present options, ask for preference |
| **ADR conflict** | Explain conflict, ask how to resolve |
| **Significant deviation from spec** | Explain why, get approval |
| **Security-sensitive code** | Flag for security review |
| **External service integration** | Confirm credentials/endpoints |
| **Database schema changes** | Confirm before applying |
| **Breaking changes to APIs** | Get approval before implementing |

### How to Request Input

```markdown
🔔 **Human Input Required**

**Situation**: {brief description}

**Question**: {specific question}

**Options** (if applicable):
1. {Option A} - {pros/cons}
2. {Option B} - {pros/cons}

**My recommendation**: {if you have one}

Please advise before I proceed.
```

---

## Operating Boundaries

### MUST Do
- Follow ADR constraints (load and check applicable ADRs)
- Write tests alongside code
- Update TASK-INDEX.md when task completes
- Create handoff when context > 70%
- Request human input for ambiguous decisions

### MUST NOT Do
- Skip required checkpoints
- Proceed when dependencies are incomplete
- Ignore ADR constraints
- Make architectural decisions without asking
- Commit directly to main branch

---

## Session Management

### Starting a Task Session

User will prompt:
```
Execute task defined in: projects/{project-name}/tasks/001-task-name.poml
```

AI should:
1. Check context (Step 0)
2. Read the task file
3. Load resources from `<knowledge>` and `<context>`
4. Follow execution protocol

### Resuming from Handoff

User will prompt:
```
Continue task from handoff: projects/{project-name}/notes/handoffs/handoff-001.md
```

AI should:
1. Read the handoff summary
2. Load listed resources
3. Continue from "Remaining" subtasks
4. Complete execution protocol

### Between Phases

Start a fresh session between project phases for optimal context usage.

---

## Quick Reference

### Essential Files to Load at Task Start
1. Task file: `projects/{project-name}/tasks/{task-file}.poml`
2. Project context: `projects/{project-name}/CLAUDE.md`
3. Task index: `projects/{project-name}/tasks/TASK-INDEX.md`

### After Task Completion
1. Update TASK-INDEX.md: Change status 🔲 → ✅
2. Document deviations (if any) in `notes/`
3. Report completion to user

---

## Related Documents

- [05-poml-reference.md](05-poml-reference.md) - POML tag definitions
- [06-context-engineering.md](06-context-engineering.md) - Context best practices

---

*Part of [Spaarke Software Development Procedures](INDEX.md) | v2.0 | December 2025*
