# Parallel Claude Code Sessions

> **Purpose**: Guide for running multiple Claude Code sessions simultaneously without merge conflicts.
>
> **Last Updated**: January 6, 2026

---

## Overview

This guide explains how to run 3-5 Claude Code sessions in parallel on the same codebase. By using **git worktrees** and following the **sequential merge pattern**, you can work on multiple projects simultaneously without code conflicts.

**Key Concepts**:
- Each Claude Code session runs in its own **worktree** (isolated directory)
- Each worktree has its own **branch** (no branch sharing)
- Conflicts are avoided by **sequential merges** (one PR at a time)
- Proactive **conflict detection** warns you before problems occur

---

## Table of Contents

1. [Core Concepts](#core-concepts)
2. [Setting Up Parallel Sessions](#setting-up-parallel-sessions)
3. [The Commit → Push → PR → Merge Flow](#the-commit--push--pr--merge-flow)
4. [Conflict Detection (Automated)](#conflict-detection-automated)
5. [When to Rebase](#when-to-rebase)
6. [Human-in-Loop Checkpoints](#human-in-loop-checkpoints)
7. [Complete Workflow Example](#complete-workflow-example)
8. [Troubleshooting](#troubleshooting)

---

## Core Concepts

### What is a Git Worktree?

A **worktree** is a separate working directory that shares the same git repository. Each worktree can be on a different branch, allowing parallel development without conflicts.

```
C:\code_files\spaarke\                    ← Main repo (master)
C:\code_files\spaarke-wt-feature-a\       ← Worktree 1 (feature/feature-a branch)
C:\code_files\spaarke-wt-feature-b\       ← Worktree 2 (feature/feature-b branch)
C:\code_files\spaarke-wt-feature-c\       ← Worktree 3 (feature/feature-c branch)
```

**Benefits**:
- Each VS Code window sees an isolated branch
- No stash juggling when switching projects
- Shared git history across all worktrees
- Claude Code sessions don't interfere with each other

### Key Definitions

| Term | Definition | When It Happens |
|------|------------|-----------------|
| **Commit** | Save a snapshot of changes locally | After completing each task or logical unit |
| **Push** | Upload local commits to GitHub | After every commit (keep remote in sync) |
| **Pull Request (PR)** | Proposed merge - visible for review but NOT merged yet | Create draft after first push for visibility |
| **Merge** | Actually combine one branch into another (usually master) | When PR is approved and ready |
| **Rebase** | Replay your commits on top of the latest master | Before merging, or when master has updates |

### The Flow Diagram

```
Your Branch                    GitHub                     Master
───────────                    ──────                     ──────
   │
   ├─ commit ─────────────────→ (local only)
   │
   ├─ push ───────────────────→ origin/your-branch
   │                                │
   ├─ create PR ──────────────────→│ PR #101 (draft)
   │                                │   │
   │  (work continues)              │   │ (reviewable)
   │                                │   │
   ├─ more commits ───────────────→│   │
   │                                │   │
   ├─ push ───────────────────────→│   │
   │                                │   │
   │  PR approved ─────────────────│   │
   │                                │   │
   └─ merge ──────────────────────────→│──────────────→ master updated
                                                        (commit abc123)
```

**Key Insight**: A PR is a *proposal* to merge. The code is NOT in master until the merge actually happens.

---

## Setting Up Parallel Sessions

### Step 1: Create Worktrees (One-Time Setup)

Run these commands from the **main repository** (not a worktree):

```powershell
# Navigate to main repo
cd C:\code_files\spaarke

# Ensure master is up to date
git fetch origin master
git checkout master
git pull origin master

# Create worktree for Session 1
git worktree add ../spaarke-wt-project-a -b work/project-a

# Create worktree for Session 2
git worktree add ../spaarke-wt-project-b -b work/project-b

# Create worktree for Session 3
git worktree add ../spaarke-wt-project-c -b work/project-c
```

### Step 2: Open Each Worktree in VS Code

```powershell
# Open Session 1 in new VS Code window
code -n C:\code_files\spaarke-wt-project-a

# Open Session 2 in new VS Code window
code -n C:\code_files\spaarke-wt-project-b

# Open Session 3 in new VS Code window
code -n C:\code_files\spaarke-wt-project-c
```

### Step 3: Start Claude Code in Each Window

Each VS Code window runs its own Claude Code session, working on its own branch.

### Naming Conventions

| Item | Convention | Example |
|------|------------|---------|
| Worktree folder | `spaarke-wt-{project-name}` | `spaarke-wt-email-automation` |
| Branch name | `work/{project-name}` | `work/email-automation` |
| Location | `C:\code_files\` (sibling to main repo) | `C:\code_files\spaarke-wt-email-automation` |

---

## The Commit → Push → PR → Merge Flow

### During a Project (Each Session)

```
┌─────────────────────────────────────────────────────────┐
│                 SINGLE SESSION WORKFLOW                  │
└─────────────────────────────────────────────────────────┘

1. WORK on task
   └─→ Claude Code implements the task

2. COMMIT (after each task)
   └─→ git add .
   └─→ git commit -m "feat(scope): description"

3. PUSH (after each commit)
   └─→ git push origin HEAD
   └─→ (Creates remote branch if first push)

4. CREATE DRAFT PR (after first push)
   └─→ gh pr create --draft --title "feat: project name"
   └─→ PR visible for team awareness

5. CONTINUE working (repeat steps 1-3)

6. WHEN DONE:
   └─→ Mark PR as "Ready for Review"
   └─→ gh pr ready

7. AFTER APPROVAL:
   └─→ Rebase if needed (see below)
   └─→ Merge PR
```

### Conflict Prevention Rules

| Rule | Why It Matters |
|------|----------------|
| **Scope isolation** | Each session works on different files/directories |
| **Frequent commits** | Smaller changes = easier conflict resolution |
| **Push after commit** | Keep remote in sync for visibility |
| **Draft PR early** | Team can see what files you're touching |
| **Rebase before merge** | Ensures clean linear history |
| **Sequential merge** | Only merge one PR at a time to master |

---

## Conflict Detection (Automated)

Claude Code has built-in conflict detection at three points:

### 1. At Project Start (project-pipeline Step 1.5)

**When**: Running `/project-pipeline` to start a new project

**What happens**:
- Claude analyzes `spec.md` to identify likely files to be modified
- Checks active PRs for overlapping files
- **Warns you** if another PR touches the same areas

**Example output**:
```
⚠️ Potential Overlap Detected

Your project (from spec.md) appears to touch:
  - src/client/pcf/ (new PCF control)
  - .claude/skills/ (skill updates)

Active PRs with overlapping files:
──────────────────────────────────
PR #98: chore: project planning updates
  Branch: work/project-planning-and-documentation
  Overlapping: .claude/skills/

Recommendations:
1. If PR #98 is close to merge → Wait for it, then start
2. If both sessions are yours → Coordinate file ownership
3. If proceeding → Plan to rebase after PR #98 merges

[Y to proceed with awareness / stop to wait]
```

**Human action**: Decide whether to proceed or wait.

### 2. At End of Each Task (task-execute Step 10.6)

**When**: After completing any task

**What happens**:
- Claude fetches latest master
- Checks if master has new commits
- Compares master changes with files you modified
- **Warns you** if there's overlap

**Example output**:
```
⚠️ Master has changes to files you modified

Files you modified:
  - src/client/pcf/CommandBar/index.ts
  - .claude/skills/dataverse-deploy/SKILL.md

Master changes since you started:
  - 3 new commits
  - Overlapping file: .claude/skills/dataverse-deploy/SKILL.md

Recommendation: Rebase before pushing to avoid conflicts

  git fetch origin master
  git rebase origin/master
  git push --force-with-lease
```

**Human action**: Run the rebase commands if overlap detected.

### 3. On-Demand (/conflict-check)

**When**: Any time you want to check for conflicts

**How to invoke**:
```
/conflict-check
```

**What happens**:
- Gathers files changed in your branch
- Gathers files changed in all active PRs
- Reports any overlaps

**Example output (no conflicts)**:
```
✅ No file conflicts detected

Your branch: work/email-automation
Files changed: 12 files
Active PRs checked: 3 PRs

Safe to proceed with merge.
```

---

## When to Rebase

### Decision Tree

```
Another PR merged to master?
│
├─ YES → Do I touch the same files as that PR?
│   │
│   ├─ YES → Rebase NOW
│   │        git fetch origin master
│   │        git rebase origin/master
│   │        git push --force-with-lease
│   │
│   └─ NO → Am I almost done with my project?
│       │
│       ├─ YES → Rebase NOW (easier while context is fresh)
│       │
│       └─ NO → Continue working, rebase before PR ready
│
└─ NO → Continue working normally
```

### Rebase Commands

```powershell
# Fetch latest master
git fetch origin master

# Rebase your branch on top of master
git rebase origin/master

# If conflicts occur:
#   1. Git will pause and show conflicting files
#   2. Edit files to resolve conflicts
#   3. git add <resolved-files>
#   4. git rebase --continue

# Push updated branch (force needed after rebase)
git push --force-with-lease
```

---

## Human-in-Loop Checkpoints

Claude Code automates most of the workflow, but **you make key decisions** at these points:

### Checkpoints Where You Decide

| Checkpoint | What Claude Does | What You Decide |
|------------|------------------|-----------------|
| **Project start (Step 1.5)** | Detects PR overlap | Proceed or wait |
| **After each step (project-pipeline)** | Shows what was generated | Y to continue, or refine |
| **End of task (Step 10.6)** | Checks for master changes | Rebase now or continue |
| **Before push** | Reviews changes | Confirm commit message |
| **Before merge** | Shows PR status | Approve and merge |

### What's Fully Automated

| Operation | Automation Level |
|-----------|-----------------|
| Task execution | Fully automated (follows POML steps) |
| Code writing | Fully automated (with ADR compliance) |
| Knowledge loading | Fully automated (from task tags) |
| Conflict detection | Fully automated (warnings only) |
| Commit message generation | Automated, user confirms |
| Rebase execution | User must run commands |
| Merge | User must approve |

---

## Complete Workflow Example

### Timeline: Two Parallel Sessions

```
TIME    SESSION 1                  SESSION 2                  MASTER
────    ─────────                  ─────────                  ──────
T1      Create worktree            Create worktree
        work/feature-a             work/feature-b

T2      /project-pipeline          /project-pipeline
        (Step 1.5: no overlap)     (Step 1.5: no overlap)

T3      Execute task 001           Execute task 001

T4      Commit + Push              Commit + Push
        Create draft PR #101       Create draft PR #102

T5      Execute task 002           Execute task 002

T6      Commit + Push              Commit + Push

T7      Execute task 003           ──working──

T8      DONE                       ──working──
        Mark PR Ready

T9      Rebase (clean)             ──working──

T10     Merge PR #101 ─────────────────────────────────────→ ← master updated

T11                                Execute task 003
                                   (Step 10.6: master changed!)

T12                                Rebase origin/master
                                   Push --force-with-lease

T13                                Execute task 004

T14                                DONE
                                   Mark PR Ready

T15                                Merge PR #102 ──────────→ ← master updated
```

### Key Observations

1. **Session 1 merges first** → Session 2 must rebase
2. **Task 10.6 detected** the master change automatically
3. **Sequential merge** prevents conflicts
4. Both sessions worked in parallel, but merged one at a time

---

## Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| "Branch is already checked out" | Branch in use by another worktree | Use a different branch name, or work in existing worktree |
| "Path already exists" | Folder already exists | Remove folder or use different name |
| Merge conflicts during rebase | Same file edited in both branches | Resolve conflicts manually, then `git rebase --continue` |
| PR won't merge | Behind master | Rebase first: `git rebase origin/master` |
| "Stale worktree" errors | Worktree folder deleted manually | Run `git worktree prune` |

### Resolving Merge Conflicts

When rebase encounters a conflict:

```powershell
# Git shows which files conflict
# Edit the files to resolve (look for <<<<<<< markers)

# After resolving each file:
git add <resolved-file>

# Continue the rebase
git rebase --continue

# If you want to abort and go back:
git rebase --abort
```

### Cleaning Up After Project Completion

```powershell
# After PR is merged, remove the worktree
cd C:\code_files\spaarke
git worktree remove ../spaarke-wt-project-a

# Optionally delete the local branch
git branch -d work/project-a

# Delete remote branch (usually done via PR merge)
git push origin --delete work/project-a
```

---

## Quick Reference

### Essential Commands

```powershell
# Create new worktree
git worktree add ../spaarke-wt-{name} -b work/{name}

# List all worktrees
git worktree list

# Check for conflicts
/conflict-check

# Rebase on master
git fetch origin master
git rebase origin/master
git push --force-with-lease

# Create draft PR
gh pr create --draft --title "feat: description"

# Mark PR ready
gh pr ready

# Merge PR
gh pr merge --squash
```

### Skills for Parallel Work

| Skill | Purpose | Trigger |
|-------|---------|---------|
| `worktree-setup` | Create/manage worktrees | `/worktree-setup`, "create worktree" |
| `conflict-check` | Detect file overlap | `/conflict-check`, "check conflicts" |
| `push-to-github` | Commit and push | `/push-to-github` |
| `pull-from-github` | Sync from remote | `/pull-from-github` |

---

## Summary

**To run parallel Claude Code sessions without conflicts:**

1. **Use worktrees** - Each session in its own directory/branch
2. **Commit frequently** - After each task
3. **Push after commits** - Keep remote in sync
4. **Create draft PRs early** - For visibility
5. **Watch for conflict warnings** - Claude detects overlaps automatically
6. **Rebase before merge** - Sync with latest master
7. **Merge one PR at a time** - Sequential merge prevents conflicts

The conflict detection system (Steps 1.5 and 10.6) provides early warning, but **you decide** when to rebase and when to proceed.

---

## Related Documentation

- [Worktree Setup Skill](.claude/skills/worktree-setup/SKILL.md) - Detailed worktree commands
- [Conflict Check Skill](.claude/skills/conflict-check/SKILL.md) - On-demand conflict detection
- [Push to GitHub Skill](.claude/skills/push-to-github/SKILL.md) - Commit and PR workflow
- [Context Recovery Procedure](context-recovery.md) - Recovering state in new sessions

---

*Last updated: January 6, 2026*
