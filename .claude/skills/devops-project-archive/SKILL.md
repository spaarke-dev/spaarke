---
description: Archive a completed or cancelled project — set Status, capture counts, close Issue, DELETE worktree, retain folder + .archived marker. DESTRUCTIVE. Per FR-14 + D-18 + NFR-09 of spaarke-devops-project-tracking-r1.
tags: [devops, project-archive, destructive, worktree-delete, gh-cli, git]
techStack: [gh-cli, graphql, git, python]
appliesTo: ["/devops-project-archive", "archive project", "close project"]
alwaysApply: false
last-reviewed: 2026-06-23
---

# devops-project-archive

> **Category**: DevOps / Portfolio
> **Tier**: Destructive — removes git worktree. Requires explicit confirmation.
> **Last Reviewed**: 2026-06-23

## ⚠️ DESTRUCTIVE ACTION WARNING

This skill **DELETES the local git worktree** for a project. Uncommitted work in the worktree will be lost if the skill is run without `--force` AND the worktree has uncommitted changes.

The `projects/{name}/` folder is RETAINED with a new `.archived` marker file per D-18 + NFR-09.

## Prerequisites

- `gh` CLI v2.40+ authenticated
- `/devops-portfolio-setup` + `/devops-project-sync` available
- A registered Project Issue exists for the target project
- Worktree exists at `c:/code_files/spaarke-wt-{slug}` (or skill no-ops on worktree deletion if missing)
- All POML tasks complete (for `--status Completed`) OR explicit user decision to cancel (for `--status Cancelled`)

## Purpose

Close out a project cleanly:
1. Set `Status` field (Completed or Cancelled)
2. Capture final `Task Count` / `Tasks Completed` values (via `/devops-project-sync`)
3. Close the GitHub Issue with appropriate state (`Status=Done` or label `cancelled`)
4. Delete the local git worktree via `git worktree remove`
5. Retain `projects/{name}/` folder
6. Write `.archived` marker file with archive date + final status + closing PR # (if any)

Remote branch is **preserved** per NFR-09 — branch history stays on GitHub for audit.

## Workflow

### Step 0: Validate inputs

Required:
- `--status <Completed|Cancelled>`

Optional:
- `--pr-number <#M>` — closing PR # (auto-detected via `gh pr list --head <branch>` if not provided)
- `--force` — proceed even if worktree has uncommitted changes
- `--folder <projects/{name}>` — defaults to current folder

### Step 1: Discover Project Issue + worktree

- Read `projects/{name}/README.md` portfolio pointer block → Issue #N
- `git worktree list` → confirm worktree path (or report no-op if already removed)

### Step 2: Safety check — uncommitted changes

```bash
cd c:/code_files/spaarke-wt-{slug}
git status --porcelain
```

If output is non-empty AND `--force` not provided:

```
ERROR: Worktree has uncommitted changes:
  M  file1.cs
  ?? file2.ts

Refusing to delete worktree. Options:
  1. Commit + push, then re-run.
  2. Stash, then re-run.
  3. Re-run with --force (uncommitted work will be LOST).
```

STOP unless `--force`.

### Step 3: Confirm prompt (mandatory)

```
About to ARCHIVE project:
  Folder:    projects/{name}/
  Worktree:  c:/code_files/spaarke-wt-{slug} (will be DELETED)
  Issue:     #N
  Status:    {Completed|Cancelled}
  PR:        #M (closing PR reference)

Proceed? [y/N]
```

Default: N. Require explicit `y` to proceed.

### Step 4: Final field sync

Invoke `/devops-project-sync` to capture latest field values (Task Count, Tasks Completed) BEFORE we close the Issue.

### Step 5: Update Issue fields + close

- Set `Status` field to `Completed` or `Cancelled` (per --status)
- **Set `Closed Date` field** (field ID: `PVTF_lAHODW0Pv84BEgWuzhWYfL4`, type DATE):
  - If `--pr-number #M` provided: use the PR's merge date (`gh pr view #M --json mergedAt --jq .mergedAt`, take the date prefix YYYY-MM-DD). This is the truest "actual end".
  - Else: use today's date.
  - Mutation: `updateProjectV2ItemFieldValue` with `value: { date: "YYYY-MM-DD" }`.
- Add comment to Issue: `Archived via /devops-project-archive on {date}. Closing PR: #M. Closed Date set to {merge-or-today}.`
- For Completed: `gh project item-edit ... --status Done` (or analogous mutation)
- For Cancelled: add label `cancelled`
- Close Issue: `gh issue close <#N> --comment "..."`

**Drift calculation note**: After this Step, both `Target Date` (set at project setup) and `Closed Date` (set here) are populated. A Project #2 view can compute drift = `Closed Date − Target Date` (positive = late, negative = early). Project status reports + `scripts/portfolio-status.py --show-drift` can surface this.

### Step 6: Delete worktree

```bash
git worktree remove c:/code_files/spaarke-wt-{slug}
git worktree list  # Verify removed
```

If `--force`, also `git worktree remove --force` if uncommitted changes were present (per Step 2 user ack).

### Step 7: Retain folder + write `.archived` marker

Write `projects/{name}/.archived`:

```
Archived: 2026-06-23
Final status: {Completed|Cancelled}
Closing PR: #M (https://github.com/spaarke-dev/spaarke/pull/M)
Worktree removed: c:/code_files/spaarke-wt-{slug}
Remote branch preserved: work/{slug} (still on origin)
Issue: #N (closed)
```

Per NFR-09 — folder + `.archived` survive; worktree gone; remote branch preserved.

### Step 8: Report

```
Archived project: projects/{name}/
  Issue #N closed (Status=Completed)
  Worktree c:/code_files/spaarke-wt-{slug} removed
  .archived marker written
  Remote branch work/{slug} preserved on origin
```

## Outputs

- Worktree deleted (irreversible without re-clone)
- `projects/{name}/.archived` marker file
- Issue #N closed with Status=Completed or Cancelled
- 1 line confirmation

## Failure Modes

| Failure | Cause | Recovery |
|---|---|---|
| Worktree has uncommitted changes; `--force` not provided | Safety gate working as designed | User stashes/commits, then re-runs |
| `git worktree remove` fails | Filesystem locked file in worktree (e.g., open file in editor) | Close editor; re-run. Or `git worktree remove --force` after ack. |
| Issue close fails (rate limit) | Concurrent operations | Re-run skill — idempotency ensures state is healed |
| `.archived` marker write fails | Permission issue | Skill warns; user manually creates the marker |
| Confirm prompt not respected | UI/script bug | Mandatory user `y` per Step 3 — this is the safety contract |
| Skill run twice on same project | User unsure if first run completed | Idempotent — second run detects worktree already gone + Issue already closed + .archived already present; reports no-op |

## Related Skills

- `/devops-project-sync` — runs before Issue close (capture final counts)
- `/devops-portfolio-status` — Completed/Cancelled projects move out of active rollup
- `/merge-to-master` — typically runs BEFORE this skill (project merged, then archived)
- `/repo-cleanup` — may prompt this skill for archive candidates (per FR-23)

## Reference

- Spec: FR-14 + D-18 (worktree delete, folder retain) + NFR-09 (remote branch preserved) + F3 (explicit gate, NOT auto-archive on merge)
- Idempotency: re-running on archived project is no-op
- Sub-Agent Write Boundary: writes to `.git/worktrees/*` + `projects/<name>/.archived` — main session only (destructive)
