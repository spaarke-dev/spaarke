---
description: Merge completed branch work into master with safety checks and build verification
tags: [git, merge, master, branches, reconciliation, operations]
techStack: [all]
appliesTo: ["merge to master", "check unmerged branches", "reconcile branches", "sync master"]
alwaysApply: false
---

# merge-to-master

> **Category**: Operations
> **Last Updated**: February 2026

---

## Purpose

Ensure completed project branch work is merged back into master so new projects always start from the latest codebase. This skill prevents the accumulation of "stranded" commits on feature branches that were pushed to origin but never merged to master.

**Problem this solves**: Feature branches are pushed to `origin` via `push-to-github`, but pushing to origin and merging to master are two different things. Without this skill, completed work stays on branches and new projects created from master start with stale code.

**Three operating modes:**

| Mode | When to Use | What It Does |
|------|-------------|--------------|
| **Audit** | Before starting new projects, periodic checks | Non-destructive scan of all branches — reports what's stranded |
| **Single Merge** | After completing a project or milestone | Merges one specific branch into master |
| **Full Reconciliation** | When multiple branches have accumulated | Merges all unmerged branches in safe order |

---

## When to Use

### Explicit Triggers (User-Invoked)

| User Says | Mode |
|-----------|------|
| "merge to master" | Single Merge (current branch) |
| "merge {branch} to master" | Single Merge (specific branch) |
| "check for unmerged branches", "audit branches" | Audit |
| "reconcile branches", "reconcile all branches" | Full Reconciliation |
| "sync master", "update master from branches" | Full Reconciliation |
| `/merge-to-master` | Audit (default) → user chooses action |

### Auto-Trigger Points (Called by Other Skills)

| Calling Skill | When | Mode |
|---------------|------|------|
| `task-execute` | After completing the **final task** in a project | Prompt: "Merge branch to master?" → Single Merge |
| `project-pipeline` | Before Step 1 (validate spec.md) | Audit only — warn if master is stale |
| `project-continue` | During Step 2 (sync with master) | Audit only — warn if branches have unmerged work |
| `push-to-github` | After successful push | Reminder: "Branch pushed. Run `/merge-to-master` when ready to merge to master." |

---

## Prerequisites

- Git repository with `origin` remote configured
- `master` branch exists locally and on origin
- User has push access to origin/master

---

## Workflow

### Step 0: Fetch and Discover

**Always runs first in all modes.**

```
FETCH:
  git fetch origin

DISCOVER all work branches:
  git branch -r | grep "origin/work/"

FOR EACH branch:
  unmerged_count = git rev-list --count origin/work/{branch} --not origin/master

  IF unmerged_count > 0:
    divergence_point = git merge-base origin/master origin/work/{branch}
    divergence_date = git log -1 --format=%ci {divergence_point}
    last_commit_date = git log -1 --format=%ci origin/work/{branch}

    ADD to report: {branch, unmerged_count, divergence_date, last_commit_date}
```

**Output:**
```
🔍 Branch Audit Report
━━━━━━━━━━━━━━━━━━━━━

| Branch | Unmerged | Diverged | Last Commit |
|--------|----------|----------|-------------|
| ai-rag-pipeline | 17 | Jan 19 | Jan 25 |
| financial-module-r1 | 19 | Feb 1 | Feb 13 |
| ... | ... | ... | ... |

Total: {N} unmerged commits across {M} branches

Already merged (0 unmerged): {list of clean branches}
```

---

### Step 1: Determine Action (Audit Mode Stops Here)

**If Audit mode**: Report findings and stop. Suggest next steps:
```
📋 Recommended Actions:
  - "merge {branch} to master" — merge a single branch
  - "reconcile all branches" — merge all {N} branches
  - No action needed — master is current ✅ (if no unmerged branches)
```

**If Single Merge mode**: Proceed to Step 2 with the specified branch.

**If Full Reconciliation mode**: Proceed to Step 2 with all unmerged branches, sorted by divergence date (oldest first).

---

### Step 2: Pre-Merge Safety Checks

```
FOR EACH branch to merge:

CHECK 1 - Working tree clean:
  git status --porcelain
  IF dirty: STOP — "Commit or stash changes before merging"

CHECK 2 - Master is up to date:
  git checkout master
  git pull origin master
  IF conflicts: STOP — "Master has conflicts with origin. Resolve first."

CHECK 3 - Divergence analysis:
  master_ahead = git rev-list --count origin/work/{branch}..origin/master
  branch_ahead = git rev-list --count origin/master..origin/work/{branch}

  REPORT: "Master is {master_ahead} commits ahead, branch has {branch_ahead} to merge"

CHECK 4 - Conflict preview:
  git merge --no-commit --no-ff origin/work/{branch}
  conflicts = git diff --name-only --diff-filter=U
  git merge --abort

  IF conflicts:
    REPORT: "{N} files will have conflicts: {list}"
    ASK: "Proceed with merge? Conflicts will need manual resolution. [y/n]"
  ELSE:
    REPORT: "Clean merge — no conflicts expected ✅"
```

---

### Step 3: Execute Merge

#### Single Branch Merge

```
git checkout master
git merge origin/work/{branch} --no-edit

IF conflicts:
  FOR EACH conflicted file:
    ANALYZE conflict type:
      - add/add (both sides created): Compare, keep richer version or merge both
      - content conflict: Examine both sides, resolve preserving all functionality
      - package-lock.json: Take master's version (npm install will regenerate)

    RESOLVE conflict
    git add {file}

  git commit  (merge commit auto-generated)

REPORT: "Merged {branch} ({N} commits) into master"
```

#### Full Reconciliation

```
CREATE reconciliation branch (safety net):
  git checkout -b reconcile/branch-cleanup master

FOR EACH branch (oldest divergence first):
  git merge origin/work/{branch} --no-edit

  IF conflicts:
    RESOLVE (same strategy as single merge)
    git commit

  REPORT progress: "✅ {branch} merged ({N}/{total} complete)"

AFTER all merges:
  PROCEED to Step 4 (verify build)
  THEN fast-forward master:
    git checkout master
    git merge reconcile/branch-cleanup --ff-only
```

---

### Step 4: Verify Build

**MANDATORY before pushing. Never push without build verification.**

```
dotnet build src/server/api/Sprk.Bff.Api/

IF build fails:
  REPORT: "❌ Build failed after merge. Errors:"
  SHOW error output
  ASK: "Fix build errors before pushing? [y/n]"

  IF yes: Fix errors, re-run build, loop until green
  IF no: STOP — do NOT push broken code to master

IF build succeeds:
  REPORT: "✅ Build passed — 0 errors, {N} warnings"
```

---

### Step 5: Push and Report

```
git push origin master

REPORT final status:
```

**Output:**
```
✅ Master Updated Successfully
━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Branches merged: {list}
Total commits integrated: {N}
Conflicts resolved: {N}
Build status: ✅ Passed

Master is now at: {commit-hash} {commit-message}

🧹 Optional cleanup:
  - Delete merged remote branches: git push origin --delete work/{branch}
  - Delete local reconciliation branch: git branch -d reconcile/branch-cleanup
```

**Wait for user** before deleting any branches.

---

### Step 6: Post-Merge Sync (Worktrees)

**If worktrees exist**, sync them with the updated master:

```
CHECK for worktrees:
  git worktree list

FOR EACH worktree on a branch that was just merged:
  REPORT: "Worktree at {path} is on branch {branch} which was merged to master"
  SUGGEST: "Consider rebasing or starting fresh from updated master"
```

---

## Conflict Resolution Strategy

The merge-to-master skill uses a consistent conflict resolution approach:

### Resolution Priority

| Conflict Type | Strategy | Rationale |
|--------------|----------|-----------|
| **add/add** (project-specific files: tasks, notes, readmes) | Take master's version | Master continued developing these after branch diverged |
| **add/add** (shared source code) | Merge both sides carefully | Both contributions likely needed |
| **content** (shared libraries) | Examine both sides, keep all functionality | Never silently drop methods, registrations, or model classes |
| **content** (Program.cs / DI registrations) | Keep ALL registrations from both sides | Missing DI = runtime failures |
| **content** (Models.cs / type definitions) | Keep ALL types from both sides | Missing types = compile errors |
| **package-lock.json** | Take master's version | Regenerated on next `npm install` |
| **package.json** | Merge both sides (may need both dependency additions) | Dependencies from both branches needed |

### Post-Merge Audit

After resolving conflicts in shared files, verify:

```
CHECK 1: No conflict markers remain
  Search for <<<<<<< in all .cs, .ts, .tsx files

CHECK 2: DI registrations complete
  Compare Program.cs endpoint mappings against branch tips

CHECK 3: Interface/implementation alignment
  Verify IDataverseService.cs methods match DataverseServiceClientImpl.cs

CHECK 4: Build compiles
  dotnet build (catches missing types, broken references)
```

---

## Error Handling

| Situation | Response |
|-----------|----------|
| Working tree dirty | STOP — ask user to commit or stash first |
| Master diverged from origin | Pull origin/master first, then proceed |
| Build fails after merge | Fix errors before pushing; never push broken code |
| Merge conflicts in >20 files | Warn user, suggest doing one branch at a time |
| Branch doesn't exist | Report error, list available branches |
| No unmerged branches found | Report "Master is current ✅" and stop |
| Worktree on merged branch | Warn user, suggest rebase or fresh checkout |
| Push rejected | Pull and retry; if force needed, ask user explicitly |

---

## Integration with Other Skills

| Skill | Integration Point | Details |
|-------|-------------------|---------|
| `push-to-github` | Post-push reminder | After pushing branch to origin, remind about merge-to-master |
| `task-execute` | Final task completion | After last project task, prompt to merge branch to master |
| `project-pipeline` | Pre-Step 1 audit | Check if master has unmerged branches before creating new project |
| `project-continue` | Step 2 audit | Check for stale master during project resumption |
| `pull-from-github` | Complementary | pull-from-github syncs branch with origin; merge-to-master flows branch into master |
| `repo-cleanup` | Post-merge cleanup | Suggest deleting merged remote branches |

---

## Related Skills

- `push-to-github` - Pushes branch to origin (prerequisite — branch must be pushed before merging)
- `pull-from-github` - Pulls latest from origin (complementary — different direction)
- `conflict-check` - Detects file overlap between PRs (useful pre-merge)
- `repo-cleanup` - Repository hygiene after merge (branch cleanup)

---

## Tips for AI

- **Audit mode is safe** — always run it first when unsure. It's read-only.
- **Merge order matters** for full reconciliation — always merge oldest-diverging branches first to minimize cascading conflicts.
- **Never skip the build step** — a compiling codebase is the minimum bar. Missing DI registrations or types cause runtime failures that are harder to debug than compile errors.
- **For add/add conflicts on shared source files**, always examine both sides. The branch likely added functionality (methods, types, registrations) that master doesn't have.
- **Program.cs is the highest-risk file** — every branch registers services and maps endpoints there. Always verify ALL registrations from ALL branches are present after merge.
- **After full reconciliation**, consider running `dotnet test` if tests exist — build passing doesn't guarantee functional correctness.
- **Worktree awareness** — if the repo uses worktrees, merged branches may still have active worktrees. Don't delete branches with active worktrees.
- **The reconciliation branch pattern** (`reconcile/branch-cleanup`) provides a safety net — if anything goes wrong, master is untouched until the final fast-forward.

---

*This skill ensures completed project work flows back into master, preventing code drift between branches and ensuring new projects always start from the latest codebase.*
