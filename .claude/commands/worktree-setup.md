# /worktree-setup

Create and manage git worktrees for parallel project development.

## Usage

```
/worktree-setup [action] [project-name]
```

**Actions**:
- `/worktree-setup new {project-name}` - Create new project worktree
- `/worktree-setup resume {project-name}` - Resume project on another computer
- `/worktree-setup list` - List all active worktrees
- `/worktree-setup remove {project-name}` - Remove completed project worktree

**Examples**:
- `/worktree-setup new email-automation` - Create worktree for new email-automation project
- `/worktree-setup resume ai-document-intel` - Setup existing project on this computer
- `/worktree-setup list` - Show all worktrees
- `/worktree-setup remove email-automation` - Clean up after project completion

## What This Command Does

This command manages git worktrees, which allow you to have multiple working directories from the same repository, each on a different branch.

**Key benefits:**
- Work on multiple projects simultaneously
- Each VS Code window has isolated branch state
- No stash juggling when switching projects
- Work across multiple computers seamlessly

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/worktree-setup/SKILL.md`
2. **Identify the action** from the argument (new, resume, list, remove)
3. **If project-name not provided**: ASK user for it
4. **Follow the appropriate workflow** from the skill

## Skill Location

`.claude/skills/worktree-setup/SKILL.md`

## Naming Conventions

| Item | Convention | Example |
|------|------------|---------|
| Worktree folder | `spaarke-wt-{project-name}` | `spaarke-wt-email-automation` |
| Branch name | `work/{project-name}` | `work/email-automation` |
| Location | `C:\code_files\` | `C:\code_files\spaarke-wt-email-automation` |

## Workflows Summary

| Workflow | When to Use |
|----------|-------------|
| **A: New** | Starting brand new project |
| **B: Resume** | Setting up existing project on different computer |
| **C: List** | See all active worktrees |
| **D: Remove** | Clean up after project is merged |

## Quick Reference

**Create new project:**
```powershell
cd C:\code_files\spaarke
git checkout master && git pull
git worktree add ..\spaarke-wt-{name} -b work/{name}
code -n ..\spaarke-wt-{name}
```

**Resume on another computer:**
```powershell
cd C:\code_files\spaarke
git fetch --all
git worktree add ..\spaarke-wt-{name} work/{name}
```

**Remove completed:**
```powershell
cd C:\code_files\spaarke
git worktree remove ..\spaarke-wt-{name}
git branch -d work/{name}
```

## Related Skills

- `project-pipeline` - Initialize project after creating worktree
- `push-to-github` - Push worktree changes to GitHub
- `pull-from-github` - Sync worktree across computers

