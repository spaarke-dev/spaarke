---
description: Merge completed branch work into master with safety checks and build verification
tags: [git, merge, master, branches, reconciliation, operations]
techStack: [all]
appliesTo: ["merge to master", "ship to master", "promote to master", "open merge PR", "auto-merge to master", "check unmerged branches", "reconcile branches", "sync master"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-16
---

# merge-to-master

> **Category**: Operations
> **Last Reviewed**: 2026-06-02
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A); auto-merge path added 2026-06-02 (R4 PR #331 ship)
> **Exemplar rationale**: Every merge is a snapshot of branch state — no canonical reference holds.

---

## Purpose

Ensure completed project branch work is merged back into master so new projects always start from the latest codebase. This skill prevents the accumulation of "stranded" commits on feature branches that were pushed to origin but never merged to master.

**Problem this solves**: Feature branches are pushed to `origin` via `push-to-github`, but pushing to origin and merging to master are two different things. Without this skill, completed work stays on branches and new projects created from master start with stale code.

**Three operating modes:**

| Mode | When to Use | What It Does |
|------|-------------|--------------|
| **Audit** | Before starting new projects, periodic checks | Non-destructive scan of all branches — reports what's stranded |
| **Single Merge** | After completing a project or milestone | Merges one specific branch into master. **If master is a protected branch (default for this repo)**, creates a PR and enables GitHub auto-merge — GitHub merges the PR automatically once required CI checks pass. **If master is unprotected**, falls back to direct local merge + push. |
| **Full Reconciliation** | When multiple branches have accumulated | Merges all unmerged branches in safe order. Always uses PR + auto-merge when master is protected — one PR per branch. |

> **Protected-branch default (added 2026-06-02)**: As of R4 PR #331, `spaarke-dev/spaarke` master enforces branch protection (PR required + 4 CI checks: `Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`, `actionlint`). Direct push is rejected. The Single Merge mode auto-detects this and uses the **PR + auto-merge** path. The legacy direct-merge path remains as a fallback for unprotected branches.

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

**FIRST: detect whether master is protected** — this determines which path runs.

```
DETECT branch protection:
  protection = gh api repos/{owner}/{repo}/branches/master/protection 2>/dev/null

  IF protection has required_status_checks OR required_pull_request_reviews:
    → master IS protected → use Path A (Auto-Merge PR — DEFAULT for this repo)
  ELSE:
    → master IS NOT protected → use Path B (Direct Local Merge — legacy fallback)
```

#### Path A: Auto-Merge PR (DEFAULT for protected master)

**Why this path**: protected master rejects direct push. GitHub's auto-merge handles the actual merge once required CI checks pass, but you must still verify locally before opening the PR so you're not relying on CI as the first line of defense.

```
STEP A.1 — Verify branch is pushed to origin (push-to-github skill auto-runs):
  current_branch = git branch --show-current
  git push origin {current_branch}
  IF push fails (e.g., needs --set-upstream): rerun with --set-upstream

STEP A.2 — Local build verification (MANDATORY before PR):
  Proceed to Step 4 (build verify). If build fails, STOP — do not open the PR.

STEP A.3 — Create the PR:
  gh pr create \
    --base master \
    --head {current_branch} \
    --title "{branch-derived title, e.g. 'Merge {project-name} into master'}" \
    --body "{see template below}"

  PR body template (HEREDOC, preserves formatting):
    ## Summary
    {1-3 bullet points describing what this branch ships}

    ## Verification
    - Local build: ✅ `dotnet build src/server/api/Sprk.Bff.Api/` 0 errors
    - {Add any additional local smoke verifications}

    ## Conflict resolution
    {If merging via this PR — document conflict decisions; "None — clean merge" if conflict-free}

    🤖 Generated with [Claude Code](https://claude.com/claude-code)

STEP A.4 — Enable auto-merge:
  gh pr merge {pr-number} --auto --merge

  This tells GitHub: "merge as a merge commit (NOT squash) automatically the moment all
  required checks pass." Choose --merge over --squash to preserve branch commit history.
  Choose --merge over --rebase unless the repo explicitly prefers linear history.

STEP A.5 — Wait for auto-merge:
  Set a Monitor watching `gh pr view {pr-number} --json state,mergedAt` for state→MERGED.
  Typical wait: time-to-CI-green (15–25 min for this repo with full Build & Test).

  IF CI fails: investigate, fix, push to PR branch — auto-merge re-evaluates automatically.
  IF auto-merge fails (e.g., new master commits cause conflicts): rebase + force-push the
  branch (with-lease), then auto-merge resumes.

STEP A.6 — Post-merge cleanup:
  Operator's main repo aligns with the new master:
    cd {main-repo-path} && git fetch origin && git checkout master && git reset --hard origin/master

REPORT: "PR {N} created (URL). Auto-merge enabled. GitHub will merge when CI green."
  After merge fires: "✅ {branch} merged into master via PR {N} (commit {sha})"
```

#### Path B: Direct Local Merge (legacy — only for unprotected master)

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

#### Path A (Auto-Merge PR) — no direct push

GitHub handles the merge via auto-merge once required CI checks pass (Step A.5 above).
Do NOT `git push origin master` on this path — direct push is rejected by branch protection.

After the auto-merge fires, run **Step A.6 post-merge cleanup** on the operator's main repo:
```
cd {main-repo-path}
git fetch origin
git checkout master
git reset --hard origin/master
```

#### Path B (Direct Local Merge) — push to master

```
git push origin master

REPORT final status:
```

**Output (both paths):**
```
✅ Master Updated Successfully
━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Branches merged: {list}
Total commits integrated: {N}
Conflicts resolved: {N}
Build status: ✅ Passed
{If Path A: PR URL + merge commit SHA}

Master is now at: {commit-hash} {commit-message}

🧹 Optional cleanup:
  - Delete merged remote branches: git push origin --delete work/{branch}
  - Delete local reconciliation branch: git branch -d reconcile/branch-cleanup
```

**Wait for user** before deleting any branches.

---

### Step 6: Post-Merge Sync (Worktrees)

**If worktrees exist**, sync them with the updated master using `worktree-sync`:

```
CHECK for worktrees:
  git worktree list

FOR EACH worktree on a branch that was just merged:
  REPORT: "Worktree at {path} is on branch {branch} which was merged to master"
  SUGGEST: "Run `/worktree-sync` in the worktree to update from master"

RECOMMENDED: Use `worktree-sync` (Update Only mode) in each active worktree
  to guarantee they have the latest master commits.
  See: .claude/skills/worktree-sync/SKILL.md
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
- `worktree-sync` - Bidirectional worktree sync (replaces manual rebase after merge)

---

## Tips for AI

- **Default to Path A (Auto-Merge PR) for this repo** — master is protected (verified 2026-06-02). Direct push is rejected and you will waste a cycle trying. Always run the branch-protection detection at Step 3 before choosing path.
- **Path A always requires local build verification BEFORE opening the PR** — CI is a safety net, not the first line of defense. If `dotnet build` fails locally, fix it before `gh pr create`, not after.
- **Use `gh pr merge --auto --merge` (not `--squash`)** — preserves the branch's commit history in master. Critical for projects with structured task commits (R4 task `XXX:` prefixes etc.).
- **One-line PR+auto-merge for already-pushed branches**: `gh pr create --title "..." --body "..." && gh pr merge --auto --merge`
- **Auto-merge does not bypass CI** — GitHub still runs all required checks. Auto-merge is "merge when checks pass," not "merge skipping checks." If checks fail, auto-merge stays pending; fix and push to re-trigger.
- **Audit mode is safe** — always run it first when unsure. It's read-only.
- **Merge order matters** for full reconciliation — always merge oldest-diverging branches first to minimize cascading conflicts.
- **Never skip the build step** — a compiling codebase is the minimum bar. Missing DI registrations or types cause runtime failures that are harder to debug than compile errors.
- **For add/add conflicts on shared source files**, always examine both sides. The branch likely added functionality (methods, types, registrations) that master doesn't have.
- **Program.cs is the highest-risk file** — every branch registers services and maps endpoints there. Always verify ALL registrations from ALL branches are present after merge.
- **After full reconciliation**, consider running `dotnet test` if tests exist — build passing doesn't guarantee functional correctness.
- **Worktree awareness** — if the repo uses worktrees, merged branches may still have active worktrees. Don't delete branches with active worktrees.
- **The reconciliation branch pattern** (`reconcile/branch-cleanup`) provides a safety net — if anything goes wrong, master is untouched until the final fast-forward.

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Merged to master without building first — broken master | Build verification step skipped because "the branch was already passing" | MANDATORY: `dotnet build src/server/api/Sprk.Bff.Api/` before push to master. CI catches issues post-push but master is broken in the meantime. |
| Pushed to master with conflict markers still in files | `git merge` produced conflicts; user resolved some but missed others | Run `git diff --check` AFTER merge resolution to catch unresolved markers (`<<<<<<<`, `=======`, `>>>>>>>`). |
| Force-pushed to master | User reached for `--force` instead of `--force-with-lease` (or worse — pushed master force) | NEVER force-push to master. NEVER use `--force` on shared branches (use `--force-with-lease` on feature branches only). |
| Deleted branch before verifying CI passed | Cleanup ran too eagerly after merge | Wait for CI green before deleting the source branch. The reconciliation pattern explicitly preserves the branch until verified. |
| Master diverges from origin after local merge | `git pull --rebase` produced conflicts; user committed without re-pushing | After merge to local master, immediately push. If push fails, fetch + rebase + push again. Don't leave local master ahead/behind silently. |
| `git push origin master` rejected with "protected branch" | Path B (Direct Local Merge) attempted against a protected master | **R4 PR #331 incident (2026-06-02)**: skill defaulted to Path B and burned ~30 min discovering branch protection mid-push. Fix: Step 3 now detects branch protection via `gh api repos/{owner}/{repo}/branches/master/protection` BEFORE executing merge, and routes to Path A (Auto-Merge PR) when protection is on. |
| Auto-merge enabled but PR never merges | Required CI checks failing OR a new master commit landed and the branch is no longer up-to-date with `strict: true` | Run `gh pr checks {N}` — if checks fail, fix + push to PR branch. If "branch out of date", merge master into the PR branch OR rebase + force-push-with-lease — auto-merge will re-evaluate. |
| Auto-merge uses squash by default (collapses commit history) | `gh pr merge --auto` without explicit `--merge` flag — gh may use repo default (often squash) | Always specify `gh pr merge --auto --merge` for projects with structured commits (R4 task `XXX:` prefixes etc.). Use `--squash` only if the repo prefers linear history and you're OK losing per-commit task fingerprints. |

---

*This skill ensures completed project work flows back into master, preventing code drift between branches and ensuring new projects always start from the latest codebase.*

---

## Portfolio Hook (added 2026-06-23 by spaarke-devops-project-tracking-r1 task 038 · FR-24)

**After merge succeeds**:

1. Capture merged PR #M (`gh pr list --head <merged-branch>` if not already known).
2. Add comment to the Project Issue: `Merged via PR #M (commit {hash})`.
3. Invoke `/devops-project-sync` to refresh fields.

**Conditional archive prompt** (per F3 — explicit gate, NOT auto-archive):

If after sync `Tasks Completed == Task Count` AND PR merged → prompt:

```
All tasks complete + PR merged. Archive project? [y/N]
```

On explicit `y`: invoke `/devops-project-archive --status Completed --pr-number #M`. Default N. **Never auto-archives** (F3 binding).

Silent on success when no archive needed. Failure degrades to ⚠️ warn.

See: [`.claude/skills/devops-project-sync/SKILL.md`](../devops-project-sync/SKILL.md), [`.claude/skills/devops-project-archive/SKILL.md`](../devops-project-archive/SKILL.md).
