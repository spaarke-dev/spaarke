# /pull-from-github

Pull latest changes from GitHub and sync your local branch.

## Usage

```
/pull-from-github
```

## What This Command Does

This command executes the `pull-from-github` skill to:
1. Check current branch and working directory state
2. Fetch latest changes from origin
3. Show what's new before pulling
4. Pull changes (using rebase for feature branches)
5. Handle any merge conflicts

## Prerequisites

Before running this command:
1. Have a git repository with a remote configured
2. Ideally have a clean working directory (uncommitted changes will be stashed)

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/pull-from-github/SKILL.md`
2. **Follow the procedure exactly** as documented in the skill
3. **Warn user** about uncommitted changes before pulling
4. **Show preview** of incoming changes before applying

## Parameters

This command takes no parameters. It will:
- Auto-detect the current branch
- Fetch and show preview of changes
- Stash uncommitted work if needed
- Use rebase for feature branches, merge for master
