# /ai-procedure-maintenance

Maintain and update AI coding procedures when adding new ADRs, constraints, patterns, protocols, or skills.

## Usage

```
/ai-procedure-maintenance [element-type]
```

**Examples**:
- `/ai-procedure-maintenance` - Show available checklists
- `/ai-procedure-maintenance adr` - Run Checklist A: New ADR
- `/ai-procedure-maintenance constraint` - Run Checklist B: New Constraint
- `/ai-procedure-maintenance pattern` - Run Checklist C: New Pattern
- `/ai-procedure-maintenance protocol` - Run Checklist D: New Protocol/AIP
- `/ai-procedure-maintenance skill` - Run Checklist E: New Skill
- `/ai-procedure-maintenance claude-md` - Run Checklist F: Root CLAUDE.md Updates
- `/ai-procedure-maintenance verify` - Run cross-reference verification only

## What This Command Does

This command ensures that when you add new elements to the AI coding procedure system, all related files are updated consistently:

- **INDEX files** are updated
- **Skill mappings** are synchronized (adr-aware, task-execute, task-create)
- **Cross-references** are verified
- **Paths** are correct (using `.claude/` structure)

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/ai-procedure-maintenance/SKILL.md`
2. **Identify the element type** from the argument (or ask user)
3. **Follow the appropriate checklist** step by step
4. **Run verification** after completing the checklist

## Skill Location

`.claude/skills/ai-procedure-maintenance/SKILL.md`

## Available Checklists

| Checklist | Element Type | Steps | Key Updates |
|-----------|--------------|-------|-------------|
| **A** | New ADR | 11 | Both ADR locations, constraints, patterns, skill mappings |
| **B** | New Constraint | 6 | Constraint file, INDEX, pattern directory, skill mappings |
| **C** | New Pattern | 5 | Pattern file, INDEX files, skill mappings |
| **D** | New Protocol | 4 | Protocol file, INDEX, CLAUDE.md embedding |
| **E** | New Skill | 7 | Skill directory, INDEX, SKILL-INTERACTION-GUIDE, CLAUDE.md |
| **F** | CLAUDE.md Update | 4 | Path verification, parallel updates, date consistency |

## Verification Checks

After any checklist, the skill runs these verifications:

1. **Path Consistency** - No old/broken paths like `docs/reference/adr`
2. **INDEX Completeness** - All files listed in their INDEX
3. **Mapping Consistency** - Skill mappings aligned across adr-aware, task-execute, task-create
4. **Date Consistency** - All modified files have current "Last Updated"

## Example: Adding ADR-023

```bash
# You just created docs/adr/ADR-023-api-rate-limiting.md
/ai-procedure-maintenance adr

# Claude Code will:
# 1. Create concise version in .claude/adr/
# 2. Update both INDEX files
# 3. Add constraints to .claude/constraints/api.md
# 4. Create pattern file if needed
# 5. Update skill mappings
# 6. Verify cross-references
```

## Related Skills

- `adr-aware` - Uses the ADR mappings this command maintains
- `task-execute` - Uses constraint/pattern mappings
- `task-create` - Uses knowledge mapping tables
- `repo-cleanup` - Can validate procedure file structure

## When to Use

Use this command when:
- Creating a new ADR
- Adding a new constraint or pattern file
- Creating or significantly modifying a skill
- Updating root CLAUDE.md with new procedures
- Discovering inconsistent references across files
