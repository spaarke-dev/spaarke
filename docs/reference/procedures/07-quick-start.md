# Quick Start: Design Spec to Completion

> **Audience**: Software Engineers starting AI-directed development  
> **Prerequisites**: Approved Design Specification (Stage 3 complete)  
> **Part of**: [Spaarke Software Development Procedures](INDEX.md)

---

## Prerequisites Checklist

Before starting, ensure you have:
- [ ] Approved Design Specification (Stage 3 complete)
- [ ] Design Spec converted to `spec.md`
- [ ] BDD scenarios (Gherkin) included in spec.md
- [ ] VS Code with Claude Code extension installed
- [ ] Access to the spaarke repository

---

## Quick Reference Workflow

```
┌─────────────────────────────────────────────────────────────────────────┐
│              DESIGN SPEC TO COMPLETION - 6 STEPS                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  STEP 1: CREATE PROJECT FOLDER                                         │
│  mkdir projects/{project-name}                                         │
│  # Copy spec.md into the folder                                        │
│                                                                         │
│  STEP 2: INITIALIZE PROJECT (Claude Code)                              │
│  "/project-init projects/{project-name}"                               │
│  ⚡ CHECKPOINT: Review README.md, plan.md                              │
│                                                                         │
│  STEP 3: CREATE TASKS (Claude Code)                                    │
│  "/task-create {project-name}"                                         │
│  ⚡ CHECKPOINT: Review task decomposition                              │
│                                                                         │
│  STEP 4: EXECUTE TASKS (Claude Code - per task)                        │
│  "Execute task: projects/{project-name}/tasks/001-xxx.poml"            │
│  ⚡ CHECKPOINT: Spot-check every 2-3 tasks                             │
│                                                                         │
│  STEP 5: VALIDATE (Developer)                                          │
│  dotnet test / npm test                                                │
│  /adr-check                                                            │
│  ✋ GATE: All tests pass, no ADR violations                            │
│                                                                         │
│  STEP 6: COMPLETE (Developer + PM)                                     │
│  Create PR → Merge → PM accepts                                        │
│  ✋ GATE: Feature accepted                                              │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Step-by-Step Details

### Step 1: Set Up Project Folder

```powershell
# Create project folder
mkdir projects/{project-name}

# Copy your spec.md into the folder
```

**Verify spec.md contains**:
- Problem statement
- Solution design
- API contracts (if applicable)
- **BDD scenarios in Gherkin format** (critical for AI)
- Files to create/modify
- ADR compliance notes

---

### Step 2: Initialize Project

**In Claude Code**:
```
/project-init projects/{project-name}
```

Or natural language:
```
Initialize the project at projects/{project-name}. 
Read spec.md and create README.md, plan.md, and CLAUDE.md.
```

**AI generates**:
- `README.md` - Project overview and graduation criteria
- `plan.md` - Implementation plan with WBS phases
- `CLAUDE.md` - AI context for this project
- `tasks/` directory with `TASK-INDEX.md`
- `notes/` directory

**Developer reviews**:
- [ ] README.md reflects project goals
- [ ] plan.md phases match design spec
- [ ] Graduation criteria are measurable

---

### Step 3: Create Tasks

**In Claude Code**:
```
/task-create {project-name}
```

**AI generates**:
- `tasks/TASK-INDEX.md` - Task registry
- `tasks/001-{name}.poml` - Phase 1 tasks
- `tasks/010-{name}.poml` - Phase 2 tasks
- etc.

**Developer reviews**:
- [ ] All phases have tasks
- [ ] Tasks are 2-4 hours each
- [ ] Dependencies are valid
- [ ] Acceptance criteria are testable

---

### Step 4: Execute Tasks

**For each task**:
```
Execute task defined in: projects/{project-name}/tasks/001-task-name.poml
```

**AI follows the [execution protocol](04-ai-execution-protocol.md)**:
1. Context check
2. Review progress
3. Gather resources
4. Plan implementation
5. Implement with tests
6. Verify
7. Update TASK-INDEX.md

**Context management**:
- If context > 70%: AI creates handoff → Start new session
- Reset sessions between phases

**Developer oversight**:
- Spot-check code every 2-3 tasks
- Intervene if AI is stuck

---

### Step 5: Validate

**Run tests**:
```powershell
# .NET
dotnet test

# TypeScript/PCF
npm test
```

**Check ADR compliance**:
```
/adr-check
```

**Verify**:
- [ ] All tests pass
- [ ] Build succeeds
- [ ] No high-priority ADR violations
- [ ] Code review complete

---

### Step 6: Complete

**Create PR**:
```powershell
git checkout -b feature/{project-name}
git add .
git commit -m "feat: {description}"
git push origin feature/{project-name}
```

**After merge**:
- [ ] Graduation criteria met
- [ ] PM accepts feature
- [ ] Clean up notes/ directory

---

## Commands Reference

| Action | Command |
|--------|---------|
| Initialize project | `/project-init projects/{name}` |
| Create tasks | `/task-create {name}` |
| Execute task | `Execute task: projects/{name}/tasks/001-task.poml` |
| Check ADRs | `/adr-check` |
| Code review | `/code-review` |
| Check context | `/context` |
| Clear context | `/clear` |

---

## Session Tips

| Situation | Action |
|-----------|--------|
| Starting new phase | Start fresh session |
| Context > 70% | Create handoff → new session |
| AI stuck | Break task into smaller pieces |
| Resuming work | Point AI to `notes/handoffs/` |

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| AI wrong pattern | Reference specific ADR |
| Tests failing | Have AI read output and fix |
| Context full | Create handoff, new session |
| Task too large | Split into subtasks |

---

## Related Documents

- [04-ai-execution-protocol.md](04-ai-execution-protocol.md) - Full execution protocol
- [05-poml-reference.md](05-poml-reference.md) - Task file format
- [06-context-engineering.md](06-context-engineering.md) - Context management

---

*Part of [Spaarke Software Development Procedures](INDEX.md) | v2.0 | December 2025*
