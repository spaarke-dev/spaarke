# /push-to-github

Commit changes and push to GitHub, optionally creating a pull request.

## Usage

```
/push-to-github
```

## What This Command Does

This command executes the `push-to-github` skill to:
1. Run pre-flight quality checks (code-review, adr-check)
2. Stage and commit changes with conventional commit format
3. Push to GitHub on a feature branch
4. Create a draft pull request via GitHub CLI

## Prerequisites

Before running this command:
1. Have uncommitted changes ready to push
2. Be authenticated with GitHub CLI (`gh auth status`)
3. Be on an appropriate branch (master or feature branch)

## Execution Instructions

**IMPORTANT**: When this command is invoked, you MUST:

1. **Load the skill**: Read `.claude/skills/push-to-github/SKILL.md`
2. **Follow the procedure exactly** as documented in the skill
3. **Get user confirmation** before committing and pushing
4. **Output the PR URL** when complete

## Parameters

This command takes no parameters. It will:
- Auto-detect the current branch
- Generate an appropriate commit message based on changes
- Create a feature branch if on master
- Ask for confirmation before destructive operations
