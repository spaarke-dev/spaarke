# /repo-cleanup

Repository hygiene audit and ephemeral file cleanup after project completion.

## Usage

```
/repo-cleanup [project-path]
```

**Examples**:
- `/repo-cleanup` - Audit entire repository
- `/repo-cleanup projects/mda-darkmode-theme` - Cleanup specific project

## What This Command Does

This command executes the `repo-cleanup` skill to:
1. Validate repository structure compliance
2. Identify and remove ephemeral files (notes/debug, notes/spikes, notes/drafts)
3. Check for orphaned files
4. Verify project completion status

## When to Use

- After completing a project
- Before merging a feature branch
- Periodic repository hygiene checks
- When `/project-status` shows a project as complete

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/repo-cleanup/SKILL.md`
2. **Scan for ephemeral files** in notes/, debug/, spikes/ directories
3. **Confirm before deleting** any files
4. **Report structure violations** if found

## Skill Location

`.claude/skills/repo-cleanup/SKILL.md`

## Ephemeral Directories

These directories contain temporary working files that should be cleaned:

| Directory | Purpose | Cleanup Rule |
|-----------|---------|--------------|
| `notes/debug/` | Debug logs, traces | Delete after issue resolved |
| `notes/spikes/` | Experimental code | Delete or promote after decision |
| `notes/drafts/` | Draft documentation | Delete or move to docs/ |
| `notes/handoffs/` | Session handoff notes | Archive or delete after handoff |

## Structure Validation

Checks for:
- Required files in project folders (spec.md, README.md, plan.md)
- Proper task file naming (NNN-slug.poml)
- Valid POML/XML structure in task files
- No orphaned files outside standard directories

## Output Format

```
📋 Repository Cleanup Report
============================

✅ Structure Validation: PASS
  - All projects have required files
  - Task files properly named

🗑️ Ephemeral Files Found: 3
  - notes/debug/api-trace-2025-12-20.log (2.3 KB)
  - notes/spikes/auth-test.ts (450 B)
  - notes/drafts/readme-v1.md (1.1 KB)

  Delete these files? [y/N]

⚠️ Warnings:
  - projects/old-feature/ has no tasks (consider archiving)

Summary: 3 files to clean, 1 warning
```

## Related Commands

- `/project-status` - Check project completion before cleanup
- `/push-to-github` - Push after cleanup
