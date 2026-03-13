---
description: Guarantee worktree is fully synchronized — committed, pushed, merged to master, updated from master
tags: [git, worktree, sync, merge, rebase, operations]
techStack: [all]
appliesTo: ["sync worktree", "worktree sync", "full sync", "sync branch with master", "update worktree from master", "make sure we have everything"]
alwaysApply: false
---

# worktree-sync

> **Category**: Operations
> **Last Updated**: March 2026

---

## Purpose

Canonical skill for bidirectional worktree synchronization. Guarantees — with verification at every step — that a worktree branch is fully committed, pushed to remote, merged to master, and updated from master.

**Problem this solves**: Running multiple projects in parallel worktrees requires frequent synchronization. Currently this requires invoking 3-4 separate skills (`push-to-github`, `merge-to-master`, `pull-from-github`) in the correct order, and the process frequently fails silently — comparing against stale refs, forgetting to sync the main repo's local master, or skipping the post-rebase force-push. One missed step means code drift, lost work, or false "up to date" reports.

**This skill replaces ad-hoc sync with a single, verified workflow.**

**Three operating modes:**

| Mode | Purpose | Steps | When to Use |
|------|---------|-------|-------------|
| **Full Sync** | Complete bidirectional sync (default) | All steps 0-6 | End of work session, before starting new project, periodic sync |
| **Push Only** | Commit + push branch to origin | Steps 0-2, 6 | Mid-session checkpoint, before switching to another worktree |
| **Update Only** | Pull latest master into worktree | Steps 0, 5-6 | Starting a work session, after another project merged to master |

---

## When to Use

### Explicit Triggers (User-Invoked)

| User Says | Mode |
|-----------|------|
| "sync worktree", "worktree sync", "full sync" | Full Sync |
| "push and sync", "commit and push" | Push Only |
| "update worktree from master", "pull master into worktree" | Update Only |
| "sync branch with master" | Full Sync |
| "make sure we have everything", "are we up to date" | Update Only (with state report) |
| `/worktree-sync` | Full Sync (default) |

### Auto-Detection Rules

| Condition | Action |
|-----------|--------|
| User is in a worktree and says "sync" or "update" | Invoke this skill |
| User asks "is this worktree current?" or "are we behind master?" | Run Step 0 (State Assessment) only |
| Starting work in a worktree (detected by `project-continue`) | Suggest Update Only mode |
| Completing final task in a project | Suggest Full Sync mode |

### Auto-Trigger Points (Called by Other Skills)

| Calling Skill | When | Mode |
|---------------|------|------|
| `project-continue` | Step 2 (sync with master) | Update Only |
| `task-execute` | After completing final task | Suggest Full Sync |
| `merge-to-master` | Step 6 (post-merge worktree sync) | Update Only |

---

## Prerequisites

- Git repository with `origin` remote configured
- Currently in a git worktree (not the main repo)
- `master` branch exists locally and on origin
- User has push access to origin

---

## Workflow

### Step 0: State Assessment

**MANDATORY first step in ALL modes. ALWAYS fetch before comparing.**

```
CRITICAL: Fetch origin FIRST — never compare against stale refs
  git fetch origin

DETECT environment:
  is_worktree = git rev-parse --is-inside-work-tree && test -f "$(git rev-parse --git-dir)/commondir"
  current_branch = git branch --show-current
  main_repo_path = resolve from git rev-parse --git-common-dir (strip /.git/worktrees/*)
  worktree_path = pwd

ASSESS state (all comparisons use FETCHED refs):
  uncommitted = git status --porcelain | wc -l
  unpushed = git rev-list --count origin/{branch}..HEAD
    IF origin/{branch} does not exist: unpushed = "all (no remote tracking branch)"
  behind_master = git rev-list --count HEAD..origin/master
  ahead_of_master = git rev-list --count origin/master..HEAD
```

**Output:**
```
📊 WORKTREE STATE ASSESSMENT
━━━━━━━━━━━━━━━━━━━━━━━━━━━
Worktree:       {worktree_path}
Branch:         {current_branch}
Main Repo:      {main_repo_path}

Uncommitted:    {N} file(s) modified
Unpushed:       {N} commit(s) ahead of origin/{branch}
Behind Master:  {N} commit(s) behind origin/master
Ahead of Master: {N} commit(s) ahead of origin/master

Mode: {Full Sync | Push Only | Update Only}
```

**CRITICAL RULE**: If `behind_master > 0`, NEVER report "up to date". Always report the exact count.

---

### Step 1: Commit All Work

**Modes: Full Sync, Push Only**

```
CHECK working tree:
  git status --porcelain

IF clean (no output):
  REPORT: "✅ Working tree clean — all work committed"
  SKIP to Step 2

IF uncommitted changes:
  SHOW: git status (full output for user review)
  SHOW: git diff --stat (summary of changes)

  IDENTIFY untracked source files:
    git ls-files --others --exclude-standard | grep -E '\.(cs|ts|tsx|js|jsx|json|md|html|css|scss|yaml|yml|xml|csproj|sln)$'
    IF any found:
      WARN: "⚠️ {N} untracked source files found — these will be LOST if not committed:"
      LIST files
      ASK: "Include these files? [y/n]"

  STAGE changes:
    git add {files}  # Specific files, not git add -A

  COMMIT:
    Generate conventional commit message from changed files
    git commit -m "{message}"

VERIFY:
  git status --porcelain
  IF NOT clean: FAIL — "❌ Commit failed — working tree still dirty"
  IF clean: REPORT: "✅ Committed: {short-sha} {message}"
```

---

### Step 2: Push to Remote

**Modes: Full Sync, Push Only**

```
CHECK remote tracking:
  git rev-parse --abbrev-ref --symbolic-full-name @{u} 2>/dev/null

IF no upstream:
  git push -u origin {branch}
ELSE:
  unpushed = git rev-list --count origin/{branch}..HEAD
  IF unpushed == 0:
    REPORT: "✅ Branch already pushed — 0 commits ahead of origin/{branch}"
    SKIP push
  ELSE:
    git push origin {branch}

IF push rejected:
  REPORT: "⚠️ Push rejected — remote has new commits"
  git fetch origin
  git rebase origin/{branch}
  IF conflicts: PAUSE — present resolution options
  git push origin {branch}

VERIFY:
  git fetch origin
  local_sha = git rev-parse HEAD
  remote_sha = git rev-parse origin/{branch}
  IF local_sha != remote_sha: FAIL — "❌ Push verification failed"
  IF local_sha == remote_sha: REPORT: "✅ Pushed: HEAD matches origin/{branch} at {short-sha}"
```

---

### Step 3: Merge to Master

**Mode: Full Sync only**

```
PRE-CHECK:
  IF unpushed > 0: FAIL — "Step 2 must complete first"

  # Check if branch is already fully merged to master
  unmerged = git rev-list --count origin/master..origin/{branch}
  IF unmerged == 0:
    REPORT: "✅ Branch already merged to master — 0 unmerged commits"
    SKIP to Step 4

MERGE STRATEGY:
  # Option A: Fast-forward merge via push (preferred — no merge commit)
  git push origin origin/{branch}:master

  IF push rejected (master has diverged):
    # Option B: Merge commit required
    REPORT: "Master has diverged — merge commit required"

    # Create temporary local master for merge
    git fetch origin master:temp-master
    git checkout temp-master
    git merge origin/{branch} --no-edit

    IF conflicts:
      REPORT: "{N} files have conflicts"
      RESOLVE using merge-to-master conflict strategy
      git add {resolved files}
      git commit

    # Build verification (MANDATORY)
    dotnet build src/server/api/Sprk.Bff.Api/ 2>/dev/null
    IF build fails:
      REPORT: "❌ Build failed after merge — fix before pushing"
      ASK user to fix
      LOOP until build passes

    git push origin temp-master:master
    git checkout {branch}
    git branch -d temp-master

VERIFY:
  git fetch origin
  # All branch commits should be reachable from origin/master
  unmerged_after = git rev-list --count origin/master..origin/{branch}
  IF unmerged_after > 0: FAIL — "❌ Merge verification failed — {N} commits still unmerged"
  IF unmerged_after == 0: REPORT: "✅ Merged to master: all commits reachable from origin/master"
```

---

### Step 4: Sync Main Repo Master

**Mode: Full Sync only**

```
RESOLVE main repo path:
  git_common_dir = git rev-parse --git-common-dir
  main_repo = dirname(dirname(git_common_dir))
  # Typically: c:\code_files\spaarke (parent of .git/worktrees/{name})

IF main_repo cannot be determined:
  WARN: "⚠️ Cannot determine main repo path — skip main repo sync"
  SKIP to Step 5

SYNC main repo:
  cd {main_repo}
  git fetch origin
  git checkout master 2>/dev/null  # May already be on master
  git pull origin master --ff-only

  IF pull fails (local master diverged):
    WARN: "⚠️ Main repo master has local commits not on origin — manual resolution needed"
    REPORT: git log --oneline origin/master..master
    ASK user how to proceed

VERIFY:
  main_master_sha = git rev-parse master
  origin_master_sha = git rev-parse origin/master
  IF main_master_sha != origin_master_sha: FAIL — "❌ Main repo master does not match origin/master"
  IF match: REPORT: "✅ Main repo master synced at {short-sha}"

RETURN to worktree:
  cd {worktree_path}
```

---

### Step 5: Update Worktree from Master

**Modes: Full Sync, Update Only**

```
REFRESH refs:
  git fetch origin

CHECK divergence:
  behind = git rev-list --count HEAD..origin/master
  IF behind == 0:
    REPORT: "✅ Branch already up to date with origin/master"
    SKIP to Step 6

STRATEGY (default: merge; rebase if user requests):
  ASK: "Branch is {behind} commits behind master. Merge or rebase? [merge/rebase]"
    Default: merge (safer, no force-push needed)

  IF merge:
    git merge origin/master --no-edit
    IF conflicts:
      REPORT: "{N} conflicts — resolve before continuing"
      RESOLVE conflicts
      git add {files}
      git commit

  IF rebase:
    git rebase origin/master
    IF conflicts:
      FOR EACH conflict:
        RESOLVE
        git add {files}
        git rebase --continue
    # Force-push required after rebase
    git push --force-with-lease origin {branch}
    REPORT: "⚠️ Force-pushed after rebase (--force-with-lease for safety)"

VERIFY:
  git fetch origin
  behind_after = git rev-list --count HEAD..origin/master
  IF behind_after > 0: FAIL — "❌ Still {N} commits behind origin/master after sync"
  IF behind_after == 0: REPORT: "✅ Branch fully up to date with origin/master"
```

---

### Step 6: Final State Report

**ALL modes — always runs last.**

```
REFRESH refs one final time:
  git fetch origin

COLLECT final state:
  uncommitted = git status --porcelain | wc -l
  unpushed = git rev-list --count origin/{branch}..HEAD 2>/dev/null || echo "N/A"
  behind_master = git rev-list --count HEAD..origin/master
  branch_sha = git rev-parse --short HEAD
  master_sha = git rev-parse --short origin/master
```

**Output:**
```
📊 WORKTREE SYNC COMPLETE
━━━━━━━━━━━━━━━━━━━━━━━━
Worktree:       {worktree_path}
Branch:         {branch} @ {branch_sha}
Master:         origin/master @ {master_sha}

Uncommitted:    {✅ None | ❌ {N} files}
Branch→Remote:  {✅ Pushed (0 ahead) | ❌ {N} unpushed}
Branch←Master:  {✅ Up to date (0 behind) | ❌ {N} behind}
Master→Origin:  {✅ Synced | ⚠️ Not checked (Push/Update mode)}
Main Repo:      {✅ Synced | ⚠️ Not checked (Push/Update mode) | ⚠️ Could not determine path}

Overall:        {✅ FULLY SYNCED | ⚠️ PARTIAL — see items above}
```

**CRITICAL**: Every line must show a verified status. Never show "✅" without having run the verification command in a prior step.

---

## Error Handling

| Situation | Response |
|-----------|----------|
| Not in a worktree | WARN: "This is the main repo, not a worktree. Use `push-to-github` or `merge-to-master` instead." |
| `git fetch` fails | STOP — network/auth issue. Suggest `dev-cleanup` skill. |
| Push rejected after rebase | Use `--force-with-lease` (NEVER `--force`). If still rejected, someone else pushed — fetch and retry. |
| Merge conflicts in Step 3 | Follow `merge-to-master` conflict resolution strategy. |
| Merge conflicts in Step 5 | Present conflicts to user, resolve interactively. |
| Build fails after merge | Fix errors before pushing to master. NEVER push broken code. |
| Main repo path not found | WARN and skip Step 4. Report in final state. |
| Branch has no remote tracking | Set upstream with `git push -u origin {branch}` in Step 2. |
| Stale refs (false "up to date") | IMPOSSIBLE if Step 0 runs correctly — `git fetch origin` is mandatory first. |

---

## Integration with Other Skills

| Skill | Integration Point | Details |
|-------|-------------------|---------|
| `merge-to-master` | Step 6 replacement | `worktree-sync` replaces the incomplete "warn about worktrees" in merge-to-master Step 6 |
| `push-to-github` | Step 10 replacement | `worktree-sync` replaces the incomplete merge-to-master flow in push-to-github Step 10 |
| `project-continue` | Step 2 enhancement | `project-continue` can delegate to `worktree-sync` Update Only for reliable sync |
| `worktree-setup` | Post-create sync | After creating a worktree, run `worktree-sync` Update Only to ensure latest master |
| `task-execute` | Final task completion | After last task, suggest `worktree-sync` Full Sync before merge-to-master |

---

## Related Skills

- `merge-to-master` - Merges branches to master (worktree-sync Step 3 uses similar patterns)
- `push-to-github` - Commits and pushes (worktree-sync Steps 1-2 use similar patterns)
- `pull-from-github` - Pulls from remote (worktree-sync Step 5 uses similar patterns)
- `project-continue` - Resumes project context (delegates to worktree-sync for branch sync)
- `worktree-setup` - Creates worktrees (worktree-sync keeps them current)

---

## Tips for AI

- **ABSOLUTE RULE: `git fetch origin` before ANY ref comparison.** The #1 cause of false "up to date" reports is comparing against stale local refs. Step 0 fetches; every subsequent step that verifies also fetches. This is intentional redundancy — better to fetch 5 times than report stale data once.
- **Never use `git push --force`** — always `--force-with-lease`. This prevents overwriting someone else's commits on the same branch.
- **Merge is safer than rebase for Step 5.** Merge doesn't require force-push and preserves history. Default to merge unless user explicitly requests rebase.
- **Build verification in Step 3 is non-negotiable.** Never push broken code to master. If `dotnet build` isn't applicable (no .NET project), skip the build check but note it in the report.
- **Main repo path resolution**: On Windows with worktrees, `git rev-parse --git-common-dir` returns something like `C:/code_files/spaarke/.git`. The main repo is the parent of `.git`. Handle both forward and back slashes.
- **The final report must be honest.** If any step was skipped (Push Only, Update Only), show "Not checked" rather than "Synced". Never infer status from a previous step — always verify with a fresh command.
- **When in doubt, re-fetch.** An extra `git fetch origin` costs milliseconds. A stale ref comparison costs hours of debugging.
- **Worktree detection**: `git rev-parse --git-common-dir` differs from `git rev-parse --git-dir` in worktrees. If they're the same, you're in the main repo, not a worktree.

---

*This skill guarantees worktree synchronization with verified state at every step — no stale refs, no silent failures, no "it should be up to date" assumptions.*
