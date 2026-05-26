---
description: Pull latest changes from GitHub and sync local branch
tags: [git, pull, sync, github, branches, operations]
techStack: [git, gh-cli]
appliesTo: ["pull from github", "git pull", "sync branch", "update from remote", "get latest"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-16
---

# Pull from GitHub

> **Category**: Operations
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A — normalized minimal frontmatter)
> **Exemplar rationale**: Pull operations target per-session branch state; no stable reference applies.

---

## Purpose

Safely pull the latest changes from GitHub, handling merge conflicts if they arise, and ensuring local work is preserved.

---

## Applies When

- User wants to update from remote
- Starting work session (should pull first)
- Before pushing to avoid conflicts
- **Trigger phrases**: "pull from github", "update from remote", "sync with github", "git pull", "get latest"

---

## Prerequisites

1. **Git configured**: Repository initialized with remote
2. **Clean working directory recommended**: Uncommitted changes may cause conflicts

---

## Worktree Support

When working in a **git worktree**, you cannot checkout master (it's checked out in the main repo). Use `origin/master` directly instead.

### Worktree Detection

```
DETECT worktree:
  git rev-parse --git-common-dir

  IF output contains ".git/worktrees":
    → Working in a worktree
    → CANNOT checkout master
    → Must rebase on origin/master directly
```

### Worktree Pull Workflow

```powershell
# 1. Fetch latest from remote
git fetch origin

# 2. Rebase current branch on origin/master (NOT local master)
git rebase origin/master

# 3. If rebase succeeds, push to remote branch
#    --force-with-lease is REQUIRED because rebase rewrites history
git push origin HEAD --force-with-lease
```

### Why --force-with-lease?

After rebasing, your local commits have new hashes. The remote branch still has the old hashes. A normal push will fail because the histories diverged. `--force-with-lease` safely overwrites the remote, but ONLY if no one else pushed in the meantime.

### Complete Worktree Update Flow

```
1. git fetch origin                      # Get latest
2. git rebase origin/master              # Rebase on origin/master
3. Resolve any conflicts if needed
4. git push origin HEAD --force-with-lease  # Update remote branch
```

---

## Workflow

### Step 1: Check Current State

```powershell
# Check current branch
git branch --show-current

# Check remote status
git remote -v

# Check for local changes
git status --porcelain
```

**Decision tree:**
```
IF has uncommitted changes:
  → WARN: "You have uncommitted changes. Options:"
    1. Stash changes, pull, then restore (recommended)
    2. Commit changes first, then pull
    3. Pull anyway (may cause conflicts)
  → ASK user preference

IF no remote configured:
  → ERROR: "No remote configured. Run: git remote add origin <url>"
  → STOP
```

### Step 2: Fetch Latest

```powershell
# Fetch from origin (doesn't modify local files yet)
git fetch origin
```

Check what's different:
```powershell
# Show commits on remote not in local
git log HEAD..origin/master --oneline

# Show summary of changes
git diff --stat HEAD origin/master
```

Present to user:
```
📥 Updates available from origin/master:
  {N} new commits
  {N} files changed
  
Recent commits:
  abc1234 feat(pcf): Add new viewer component
  def5678 fix(api): Handle null response
  
Proceed with pull? (y/n)
```

### Step 3: Pull Strategy

**Option A: Fast-forward merge (cleanest)**
```powershell
# Only works if no local commits ahead
git pull --ff-only origin master
```

**Option B: Rebase (keeps linear history - preferred for feature branches)**
```powershell
git pull --rebase origin master
```

**Option C: Merge (creates merge commit)**
```powershell
git pull origin master
```

**Spaarke default**: Use rebase for feature branches, merge for master.

### Step 4: Handle Conflicts (if any)

```
IF merge conflicts occur:
  → LIST conflicted files
  → For each file:
    → Show conflict markers
    → ASK user how to resolve:
      1. Keep local version
      2. Keep remote version  
      3. Manual merge (open in editor)
  → After resolution:
    git add {resolved-files}
    git rebase --continue  # or git merge --continue
```

### Step 5: Verify

```powershell
# Confirm current state
git status

# Show recent history
git log --oneline -5
```

Success message:
```
✅ Successfully pulled from origin/master
  {N} commits pulled
  {N} files updated
  
Your local branch is now up to date.
```

---

## Stash Workflow

If user has uncommitted changes:

```powershell
# Save current work
git stash push -m "WIP: {description}"

# Pull updates
git pull --rebase origin master

# Restore work
git stash pop

# If stash conflicts:
# Resolve conflicts, then:
git stash drop
```

---

## Common Scenarios

### Starting a Work Session
```
1. git fetch origin
2. git status (check for local changes)
3. git pull --rebase origin master
4. Ready to work!
```

### Before Pushing
```
1. git fetch origin
2. Check if behind: git log HEAD..origin/master --oneline
3. If behind: git pull --rebase origin master
4. Then push
```

### Updating Feature Branch from Master

**Standard repo:**
```
1. git checkout master
2. git pull origin master
3. git checkout feature-branch
4. git rebase master
```

**Worktree (CANNOT checkout master):**
```
1. git fetch origin
2. git rebase origin/master
3. git push origin HEAD --force-with-lease
```

---

## Error Handling

| Error | Solution |
|-------|----------|
| "Your local changes would be overwritten" | Stash or commit local changes first |
| "Automatic merge failed" | Resolve conflicts manually |
| "Cannot rebase: uncommitted changes" | Commit or stash changes |
| "fatal: refusing to merge unrelated histories" | Use `--allow-unrelated-histories` (rare) |
| "Connection refused" | Check network, VPN, or GitHub status |

---

## Related Skills

- `push-to-github` - For pushing after pulling (includes linting pre-flight)
- `code-review` - Review pulled changes if needed (includes linting check)
- `ci-cd` - Monitor CI status after pushing rebased branch

---

## Tips for AI

- Always detect if working in a worktree before suggesting `git checkout master`
- In worktrees, use `git rebase origin/master` (not local master)
- After rebase in worktree, remind user that `--force-with-lease` is needed for push
- If rebase has conflicts, guide user through resolution before push
- Check `git status` after rebase to confirm clean state before push

### Worktree-Specific Tips

- **NEVER** suggest `git checkout master` in a worktree - it will fail
- Use `git rev-parse --git-common-dir` to detect worktree
- After successful rebase + push, CI will run on the updated branch
- If user says "update my branch" or "get latest", this means: fetch + rebase origin/master + force push

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| `git checkout master` run in a worktree — fails with "branch already checked out" | Skill or user forgot the worktree constraint | NEVER `git checkout master` in a worktree. Detect via `git rev-parse --git-common-dir`. If user wants master, switch to main repo first. |
| Pull overwrites local uncommitted changes | User had work-in-progress that wasn't stashed | ALWAYS stash before pull (Stash Workflow section). On pull conflict, prefer `git stash pop` AFTER resolution, not before. |
| Rebase produced silent merge conflicts that built clean but broke tests | `--strategy-option=theirs` or `ours` chose one side automatically | NEVER use `-X theirs` or `-X ours` unless explicitly justified. Manual resolution is safer than auto-pick. |
| Force-with-lease push rejected — "stale info" | Remote has commits the local fetch didn't see | Re-fetch (`git fetch`), then re-rebase, then push. Don't escalate to `--force` (which discards remote work). |
| Pulled latest but CI shows stale status | gh CLI cache hit | Run `gh run list --limit 5 --branch <branch>` to force-refresh. Don't trust the CI badge for branches you just updated. |
