# design-to-project - ARCHIVED

> **Status**: ARCHIVED
> **Date Archived**: December 18, 2024
> **Reason**: Superseded by refactored skill architecture

---

## Why This Skill Was Archived

The `design-to-project` skill has been **superseded by a cleaner two-tier architecture**:

1. **project-setup** (Tier 1 - Component) - Artifact generation
2. **project-pipeline** (Tier 2 - Orchestrator) - Full pipeline with human-in-loop

### Problems with design-to-project

1. **Overlap with project-init**: Both skills generated artifacts (README, PLAN, CLAUDE.md) with nearly identical logic
2. **Overlap with project-pipeline**: Both skills orchestrated full project initialization, creating user confusion
3. **Phase 5 (Implementation)**: Attempted to execute all tasks automatically, which wasn't practical
4. **Resource discovery duplication**: Duplicated logic between design-to-project Phase 2 and project-init Step 2.5
5. **Feature branch duplication**: Feature branching logic duplicated in design-to-project and project-init
6. **Path inconsistency**: Used `docs/projects/` in some places and `projects/` in others
7. **Unclear recommendation**: INDEX.md marked project-pipeline as RECOMMENDED, but design-to-project was more comprehensive

### Migration Path

| Old Usage | New Usage |
|-----------|-----------|
| `/design-to-project projects/{name}` | `/project-pipeline projects/{name}` (recommended) |
| Manual artifact generation | `/project-setup projects/{name}` (advanced) |
| Task execution from design-to-project Phase 5 | Use `task-execute` for each task individually |

---

## What Replaced It

### New Architecture

```
┌─────────────────────────────────────────────────────────┐
│                   NEW SKILL ARCHITECTURE                │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  Tier 1: project-setup (Component)                      │
│  ├─ Purpose: Pure artifact generation                  │
│  ├─ Input: spec.md                                     │
│  ├─ Output: README, PLAN, CLAUDE.md, folders           │
│  ├─ No resource discovery                              │
│  ├─ No branching                                       │
│  └─ Called by orchestrators or used standalone         │
│                                                         │
│  Tier 2: project-pipeline (Orchestrator) ⭐ RECOMMENDED │
│  ├─ Purpose: Full spec → ready tasks pipeline          │
│  ├─ Calls: project-setup (Tier 1)                      │
│  ├─ Human-in-loop: After each major step               │
│  ├─ Resource discovery: Before calling project-setup   │
│  ├─ Feature branch: After tasks created                │
│  └─ Optional: Auto-start task 001                      │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### Key Improvements

| Improvement | Benefit |
|-------------|---------|
| **Clear separation of concerns** | project-setup = artifacts, project-pipeline = orchestration |
| **Single source of truth** | Artifact generation logic lives in one place (project-setup) |
| **No duplication** | Resource discovery, branching, task creation in one place |
| **Better UX** | Human-in-loop confirmations at natural checkpoints |
| **Flexible** | Can use project-setup standalone or via project-pipeline |
| **Maintainable** | Changes to artifact generation happen in project-setup only |

---

## Mapping design-to-project to New Skills

| design-to-project Phase | New Skill(s) |
|------------------------|--------------|
| Phase 1: Ingest | project-pipeline Step 1 (Validate SPEC.md) |
| Phase 2: Context | project-pipeline Step 2 Part 1 (Resource Discovery) |
| Phase 3: Generate | project-pipeline Step 2 Part 2 (calls project-setup) + Step 3 (task-create) |
| Phase 4: Validate | Integrated into project-pipeline checkpoints |
| Phase 5: Implement | **REMOVED** - Use task-execute for each task individually |

### Why Phase 5 (Implementation) Was Removed

The original design-to-project included Phase 5 to automatically execute all tasks. This was **removed** because:

1. **Context limits**: Executing all tasks in one session often hit context limits
2. **Handoff complexity**: Required complex handoff protocols mid-implementation
3. **User control**: Users want to control when tasks execute, not automatic execution
4. **Better pattern**: task-execute handles individual task execution with proper context loading

**New approach**: project-pipeline stops after creating tasks. Users then execute tasks individually:
```
/project-pipeline projects/my-project  # Full setup
execute task 001                       # Execute first task
execute task 002                       # Execute second task
...
```

---

## Historical Context

### design-to-project Phases (Original)

1. **Phase 1: INGEST** - Extract key info from design spec
2. **Phase 2: CONTEXT** - Gather ADRs, architecture, existing code
3. **Phase 3: GENERATE** - Create README, plan.md, task files (called project-init + task-create)
4. **Phase 4: VALIDATE** - Cross-reference checklist before coding
5. **Phase 5: IMPLEMENT** - Execute tasks with context management

### Lines of Code
- design-to-project: 523 lines
- project-init (old): 296 lines
- **project-setup (new)**: ~350 lines
- **project-pipeline (enhanced)**: ~450 lines

**Result**: Total lines reduced from 819 to 800, but with cleaner separation and no duplication.

---

## If You Need the Old Behavior

If you need the comprehensive 5-phase pipeline with automatic implementation:

1. **Use project-pipeline** for project setup
2. **Create a shell script or task** to loop through tasks:
   ```bash
   for task in projects/{name}/tasks/*.poml; do
     echo "Executing $task..."
     # Invoke task-execute for each task
     # This gives you similar automatic execution
   done
   ```

But the recommended approach is:
- Use project-pipeline for setup
- Execute tasks individually with task-execute
- This provides better control and avoids context issues

---

## References

- **Replacement Skills**:
  - [project-setup](.claude/skills/project-setup/SKILL.md)
  - [project-pipeline](../../project-pipeline/SKILL.md)
  - [task-execute](../../task-execute/SKILL.md)

- **Skill Interaction Guide**: [SKILL-INTERACTION-GUIDE.md](../SKILL-INTERACTION-GUIDE.md)
- **Skills INDEX**: [INDEX.md](../../INDEX.md)

---

*This skill is archived for historical reference. Do not use in new work. Use project-pipeline instead.*
