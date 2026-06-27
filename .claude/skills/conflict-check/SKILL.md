---
description: Detect potential file conflicts between active PRs and current work; auto-trigger on hot-path-watchlist matches
tags: [git, conflict, parallel-development, operations, hot-path]
techStack: [git, gh-cli]
appliesTo: ["parallel sessions", "before merge", "conflict detection", "hot-path edits"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-06-26
---

# Conflict Check

> **Category**: Operations
> **Last Reviewed**: 2026-06-26
> **Reviewed By**: ci-cd-unit-test-remediation-r1 task CICD-031 (Stream C — added Hot-Path Watchlist + Auto-Trigger Criteria per spec FR-C03)
> **Exemplar rationale**: Conflict detection runs against ephemeral PR state and live branches — no stable artifact to reference.

---

## Purpose

Proactively detect potential merge conflicts by comparing files changed in your current branch with:
1. Files changed in other active PRs
2. Files changed in master since your branch diverged

**Use Cases**:
- Before starting a new project (check for overlapping work)
- Before marking a PR "Ready for Review"
- After another PR merges to master
- When running parallel Claude Code sessions

---

## Applies When

- Running multiple Claude Code sessions in parallel
- Before merging a PR
- User says "check for conflicts", "conflict check", "file overlap"
- **Trigger phrases**: `/conflict-check`, "check conflicts", "file overlap"

---

## Workflow

### Step 1: Gather Current Branch Info

```powershell
# Get current branch name
git branch --show-current

# Get files changed in current branch vs master
git fetch origin master
git diff --name-only origin/master...HEAD
```

Store as: `current_branch_files`

### Step 2: Get Active PRs

```powershell
# List all open PRs (excluding current branch)
gh pr list --state open --json number,title,headRefName,files
```

**Parse output:**
```
FOR EACH PR:
  - PR number
  - Title
  - Branch name
  - Files changed
```

### Step 3: Detect Overlaps

```
FOR EACH active PR:
  overlap = intersection(current_branch_files, pr_files)

  IF overlap is not empty:
    ADD to conflicts list:
      - PR #, title, branch
      - Overlapping files
```

### Step 4: Check Master Divergence

```powershell
# Files changed in master since branch diverged
git log origin/master --name-only --pretty=format: --since="$(git log -1 --format=%ci HEAD~10)" | sort -u
```

Compare with `current_branch_files` for additional overlap risk.

### Step 5: Present Results

**If no conflicts:**
```
✅ No file conflicts detected

Your branch: {branch-name}
Files changed: {N} files
Active PRs checked: {M} PRs

Safe to proceed with merge.
```

**If conflicts detected:**
```
⚠️ Potential Conflicts Detected

Your branch: {branch-name}
Files changed: {N} files

Overlapping PRs:
──────────────────
PR #{number}: {title}
  Branch: {branch}
  Overlapping files:
    - src/server/api/SomeFile.cs
    - src/client/pcf/Component/index.ts

PR #{number2}: {title2}
  Branch: {branch2}
  Overlapping files:
    - CLAUDE.md

Recommendations:
1. Coordinate with PR owners on shared files
2. Consider merging one PR first, then rebase
3. If same session: designate file ownership

Run rebase after other PR merges:
  git fetch origin master
  git rebase origin/master
  git push --force-with-lease
```

---

## Hot-Path Watchlist & Auto-Trigger Criteria

**Added 2026-06-26** by `ci-cd-unit-test-remediation-r1` task CICD-031 per spec FR-C03. The watchlist is consumed by `task-execute` Step 0.5 (modified in task CICD-060) which auto-invokes `/conflict-check` when any changed file in a task matches a watchlist entry.

### Hot-Path Watchlist (machine-readable for task-execute Step 0.5)

| Hot path | Glob pattern(s) | Coordination target |
|---|---|---|
| **BFF API** | `src/server/api/Sprk.Bff.Api/**`, `src/server/shared/Spaarke.Core/**`, `src/server/shared/Spaarke.Dataverse/**` | Other active worktrees touching BFF; check `projects/INDEX.md` BFF column |
| **BFF entry/DI** | `src/server/api/Sprk.Bff.Api/Program.cs`, `src/server/api/Sprk.Bff.Api/*.csproj`, `src/server/api/Sprk.Bff.Api/Services/Ai/*Module.cs` | Highest-conflict files — sequence merges via INDEX.md |
| **SpaarkeAi code page** | `src/solutions/SpaarkeAi/**`, especially `src/solutions/SpaarkeAi/src/*Registry*`, `src/solutions/SpaarkeAi/package.json` | Other active worktrees touching SpaarkeAi; check `projects/INDEX.md` SpaarkeAi column |
| **CI workflows** | `.github/workflows/**` | Only one active project should own CI changes at a time (currently `ci-cd-unit-test-remediation-r1`) |
| **Skill directives** | `.claude/skills/**`, `.claude/constraints/**` | Serial PRs only — never parallel edits to the same SKILL.md |
| **Root CLAUDE.md** | `CLAUDE.md` (repo root only) | Single-file edit; coordinate via INDEX.md |

### Auto-Trigger Criteria

`task-execute` Step 0.5 invokes `/conflict-check` automatically when **ANY** of the following conditions hold for the current task:

1. Task POML's `<outputs>` or `<inputs>` reference a file matching any watchlist glob.
2. Task execution at any step modifies a file matching any watchlist glob (detected via `git status` / `git diff --name-only` after each step).
3. Task POML metadata `<tags>` contains: `bff-api`, `spaarke-ai`, `ci-workflows`, `skill-directive`, `root-claude`.

When triggered, the conflict-check runs the standard Steps 1–5 plus an additional **Step 6: Cross-reference projects/INDEX.md** — for each matched hot-path, lookup other active worktrees with the same hot-path declaration (BFF=YES, SpaarkeAi=YES, etc.) and surface them as coordination targets.

### Decision: silent pass vs. loud warn

| Scenario | Outcome |
|---|---|
| Watchlist match BUT no other active worktree shares the hot-path | Silent log: `✅ Hot-path {X} touched — no concurrent active projects on this surface.` |
| Watchlist match AND another active worktree shares the hot-path AND no overlapping files between PRs | Soft warn: `⚠️ Hot-path {X} also owned by {project-Y}. No file overlap yet — coordinate ordering.` |
| Watchlist match AND another active worktree shares the hot-path AND files overlap | **Hard warn + escalation**: `🛑 Hot-path {X} overlap with {project-Y} on files {list}. Coordinate before proceeding.` |

This skill does NOT block task execution on its own — it surfaces information. The decision to proceed, coordinate, or defer is the task author's. (Per autonomous-mode rules of projects that have explicitly opted in, the skill can be informational only.)

### Maintenance contract for `projects/INDEX.md`

- `project-pipeline` skill adds a new project's row at start-of-project (per task CICD-061 modification).
- `task-execute` Step 0.5 reads INDEX.md at every task start; updates the row when hot-path declaration drifts.
- No cron; updates are atomic and event-driven.

### Reference

- Source: `ci-cd-unit-test-remediation-r1` spec FR-C03 + design.md §86-89
- Consumer skills: `task-execute` Step 0.5 (CICD-060), `project-pipeline` Step 2 (CICD-061)
- Constraint cross-reference: `.claude/constraints/bff-extensions.md` Hot-Path Declaration section (CICD-062)

---

## Quick Commands

```powershell
# Full conflict check (run this skill)
/conflict-check

# Manual: See files in other PRs
gh pr list --json number,title,files --jq '.[] | "\(.number): \(.title) - \(.files | length) files"'

# Manual: See specific PR files
gh pr view 101 --json files --jq '.files[].path'

# Manual: Compare branches
git diff --name-only HEAD origin/feature/other-branch

# Manual: See master changes since you branched
git log origin/master --name-only --oneline HEAD..origin/master
```

---

## Integration Points

### With project-pipeline

At project planning time, check for overlapping work:

```
DURING Step 1 (Validate SPEC.md):
  → Identify likely files to be modified (from spec scope)
  → Run /conflict-check to detect overlap with active PRs
  → WARN if significant overlap detected
  → ASK: Proceed anyway, coordinate, or wait?
```

### With task-execute

At end of each task:

```
AFTER task completion, BEFORE commit:
  → git fetch origin master
  → Check if master has changes to files you modified
  → IF overlap: Recommend rebase before push
```

### With push-to-github

Before creating/updating PR:

```
DURING Step 1 (Pre-flight checks):
  → Run conflict check
  → IF conflicts: WARN and recommend resolution strategy
```

---

## Decision Tree

```
Running /conflict-check
│
├─ Fetch current branch files
│
├─ Fetch active PR files
│
├─ Calculate overlaps
│
└─ Overlaps found?
    │
    ├─ NO → "✅ Safe to proceed"
    │
    └─ YES → Show overlapping files
        │
        ├─ Same session owns both → "Merge PR #{X} first, then rebase"
        │
        ├─ Different owners → "Coordinate with PR owner"
        │
        └─ Can split work → "Consider file ownership split"
```

---

## Example Output

**User**: `/conflict-check`

**Output (no conflicts)**:
```
✅ No file conflicts detected

Your branch: feature/dark-mode
Files changed: 12 files
Active PRs checked: 3 PRs

Safe to proceed with merge.
```

**Output (conflicts found)**:
```
⚠️ Potential Conflicts Detected

Your branch: feature/dark-mode
Files changed: 12 files

Overlapping PRs:
──────────────────
PR #98: chore: project planning updates
  Branch: work/project-planning-and-documentation
  Overlapping files:
    - .claude/skills/INDEX.md
    - CLAUDE.md

Recommendations:
1. PR #98 is close to merge - wait for it to complete
2. After merge: git fetch origin && git rebase origin/master
3. Then push your updated branch
```

---

## Related Skills

- `worktree-setup` - Parallel session management
- `push-to-github` - Pre-push conflict detection
- `project-pipeline` - Project planning overlap detection
- `task-execute` - End-of-task sync check

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| `gh pr list` returns stale data — recent PRs missed | gh CLI uses cached state; new PR opened in the last few minutes not visible | Force-refresh with `gh pr list --state open --json number,title,headRefName,files` (no cache flag needed when JSON-formatted). If still stale, `gh api repos/{owner}/{repo}/pulls --jq '.[].head.ref'` bypasses cache entirely. |
| Detected "overlap" turns out to be a non-conflict (e.g., both PRs add to the same file in different lines) | File-level intersection check is too coarse — line-level conflict only shows up at merge time | Use the detected overlap as a SIGNAL to coordinate, not an absolute block. Two PRs editing the same file can usually coexist if scope is clearly partitioned. |
| Missed overlap — two parallel sessions modify the same file without warning | Check wasn't run, OR PR not yet opened (work is local-only) | Run conflict-check at session start AND before any major file modification. For pre-PR local work, scan other worktrees: `git worktree list` + `git status` in each. |
| False positive on "stale" worktree | Worktree has uncommitted but legitimate work-in-progress | Don't auto-cleanup based on conflict-check signals — let the user decide. The skill warns; the user resolves. |

---

*This skill enables proactive conflict detection for parallel Claude Code development workflows.*
