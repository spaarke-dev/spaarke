---
description: Pull latest changes from GitHub and sync local branch
alwaysApply: false
---

# Pull from GitHub

> **Category**: Operations
> **Last Updated**: December 2025

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
```
1. git checkout master
2. git pull origin master
3. git checkout feature-branch
4. git rebase master
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

- `push-to-github` - For pushing after pulling
- `code-review` - Review pulled changes if needed
